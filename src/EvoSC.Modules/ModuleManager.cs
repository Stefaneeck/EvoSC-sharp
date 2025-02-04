﻿using System.ComponentModel;
using System.Data.Common;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Config.Net;
using EvoSC.Common.Config.Models;
using EvoSC.Common.Config.Stores;
using EvoSC.Common.Controllers.Attributes;
using EvoSC.Common.Interfaces.Controllers;
using EvoSC.Common.Interfaces.Middleware;
using EvoSC.Common.Interfaces.Models;
using EvoSC.Common.Interfaces.Services;
using EvoSC.Common.Middleware;
using EvoSC.Common.Middleware.Attributes;
using EvoSC.Common.Permissions.Attributes;
using EvoSC.Common.Permissions.Models;
using EvoSC.Common.Util;
using EvoSC.Common.Util.EnumIdentifier;
using EvoSC.Modules.Attributes;
using EvoSC.Modules.Exceptions;
using EvoSC.Modules.Exceptions.ModuleServices;
using EvoSC.Modules.Extensions;
using EvoSC.Modules.Interfaces;
using EvoSC.Modules.Models;
using EvoSC.Modules.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Container = SimpleInjector.Container;

namespace EvoSC.Modules;

public class ModuleManager : IModuleManager
{
    private readonly ILogger<ModuleManager> _logger;
    private readonly IControllerManager _controllers;
    private readonly IModuleServicesManager _servicesManager;
    private readonly DbConnection _db;
    private readonly IActionPipelineManager _pipelineManager;
    private readonly IPermissionManager _permissions;
    private readonly IEvoScBaseConfig _config;

    private readonly Dictionary<Guid, IModuleLoadContext> _loadedModules = new();
    private readonly Dictionary<string, Guid> _moduleNameMap = new();

    public IReadOnlyList<IModuleLoadContext> LoadedModules => _loadedModules.Values.ToList();
    
    public ModuleManager(ILogger<ModuleManager> logger, IEvoScBaseConfig config, IControllerManager controllers,
        IModuleServicesManager servicesManager, IActionPipelineManager pipelineManager, DbConnection db,
        IPermissionManager permissions)
    {
        _logger = logger;
        _config = config;
        _controllers = controllers;
        _servicesManager = servicesManager;
        _db = db;
        _pipelineManager = pipelineManager;
        _permissions = permissions;
        
        WarnForDisabledVerification();
    }

    private void WarnForDisabledVerification()
    {
        if (!_config.Modules.RequireSignatureVerification)
        {
            _logger.LogWarning("Signature verification for modules is disabled");
        }
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task InstallAsync(Guid loadId)
    {
        var moduleContext = GetModule(loadId);

        await InstallPermissions(moduleContext);
        await TryCallModuleInstall(moduleContext);
        
        _logger.LogDebug("Module {Type}({Module}) was installed", moduleContext.MainClass, loadId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task UninstallAsync(Guid loadId)
    {
        var moduleContext = GetModule(loadId);

        await UninstallPermissions(moduleContext);
        await TryCallModuleUninstall(moduleContext);
        
        _logger.LogDebug("Module {Type}({Module}) was uninstalled", moduleContext.MainClass, loadId);
    }

    public IModuleLoadContext GetModule(Guid loadId)
    {
        if (loadId == Guid.Empty || !_loadedModules.ContainsKey(loadId))
        {
            throw new EvoScModuleException($"Module with Id {loadId} does not exist.");
        }

        var moduleContext = _loadedModules[loadId];
        return moduleContext;
    }
    
    private Task RegisterPermissionsAsync(IModuleLoadContext loadContext)
    {
        foreach (var assembly in loadContext.Assemblies)
        {
            foreach (var permGroup in assembly.AssemblyTypesWithAttribute<PermissionGroupAttribute>())
            {
                var rootName = permGroup.Name;

                var idAttr = permGroup.GetCustomAttribute<IdentifierAttribute>();
                if (idAttr != null)
                {
                    rootName = idAttr.Name;
                }

                foreach (var f in permGroup.GetFields())
                {
                    if (f.FieldType != permGroup)
                    {
                        continue;
                    }

                    var actualName = f.GetCustomAttribute<IdentifierAttribute>()?.Name ?? f.Name;
                
                    loadContext.Permissions.Add(new Permission
                    {
                        Name = $"{rootName}.{actualName}",
                        Description = f.GetCustomAttribute<DescriptionAttribute>()?.Description ?? ""
                    });
                }
            }
        }

        return Task.CompletedTask;
    }

    private async Task InstallPermissions(IModuleLoadContext moduleContext)
    {
        var identifiedPermissions = new List<IPermission>();
        
        foreach (var permission in moduleContext.Permissions)
        {
            var existingPermission = await _permissions.GetPermission(permission.Name);

            if (existingPermission != null)
            {
                _logger.LogDebug("Wont install permission '{Name}' as it already exists", permission.Name);
                identifiedPermissions.Add(existingPermission);
                continue;
            }

            _logger.LogDebug("Installing permission: {Name}", permission.Name);
            await _permissions.AddPermission(permission);
            var identifiedPermission = await _permissions.GetPermission(permission.Name);

            if (identifiedPermission == null)
            {
                _logger.LogError(
                    "Could not identify permission '{Name}' after installing it. Was it not added to the database?",
                    permission.Name);
                continue;
            }
            
            identifiedPermissions.Add(identifiedPermission);
        }

        moduleContext.Permissions = identifiedPermissions;
    }

    private async Task UninstallPermissions(IModuleLoadContext moduleContext)
    {
        foreach (var permission in moduleContext.Permissions)
        {
            await _permissions.RemovePermission(permission);
        }
    }

    private Task EnableMiddlewares(IModuleLoadContext moduleContext)
    {
        _pipelineManager.AddPipeline(PipelineType.ChatRouter, moduleContext.LoadId,
            moduleContext.Pipelines[PipelineType.ChatRouter]);
        _pipelineManager.AddPipeline(PipelineType.ControllerAction, moduleContext.LoadId,
            moduleContext.Pipelines[PipelineType.ControllerAction]);

        return Task.CompletedTask;
    }

    private Task DisableMiddlewares(IModuleLoadContext moduleContext)
    {
        _pipelineManager.RemovePipeline(PipelineType.ChatRouter, moduleContext.LoadId);
        _pipelineManager.RemovePipeline(PipelineType.ControllerAction, moduleContext.LoadId);

        return Task.CompletedTask;
    }

    private Task RegisterMiddlewaresAsync(IModuleLoadContext moduleContext)
    {
        foreach (var assembly in moduleContext.Assemblies)
        {
            foreach (var middlewareType in assembly.AssemblyTypesWithAttribute<MiddlewareAttribute>())
            {
                var attr = middlewareType.GetCustomAttribute<MiddlewareAttribute>();
                moduleContext.Pipelines[attr!.For].AddComponent(middlewareType, moduleContext.Services);
            }
        }

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task TryCallModuleEnable(IModuleLoadContext moduleContext)
    {
        if (moduleContext.Instance is IToggleable instance)
        {
            return instance.Enable();
        }

        return Task.CompletedTask;
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task TryCallModuleDisable(IModuleLoadContext moduleContext)
    {
        if (moduleContext.Instance is IToggleable instance)
        {
            return instance.Disable();
        }

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task TryCallModuleInstall(IModuleLoadContext moduleContext)
    {
        if (moduleContext.Instance is IInstallable instance)
        {
            return instance.Install();
        }

        return Task.CompletedTask;
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    private Task TryCallModuleUninstall(IModuleLoadContext moduleContext)
    {
        if (moduleContext.Instance is IInstallable instance)
        {
            return instance.Uninstall();
        }

        return Task.CompletedTask;
    }

    private Task EnableControllers(IModuleLoadContext moduleContext)
    {
        foreach (var assembly in moduleContext.Assemblies)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.GetTypes())
                {
                    var controllerAttr = type.GetCustomAttribute<ControllerAttribute>();

                    if (controllerAttr == null || !type.IsControllerClass())
                    {
                        continue;
                    }
                
                    _controllers.AddController(type, moduleContext.LoadId, moduleContext.Services);
                }
            }
        }

        return Task.CompletedTask;
    }

    private Task DisableControllers(IModuleLoadContext moduleContext)
    {
        _controllers.RemoveModuleControllers(moduleContext.LoadId);
        return Task.CompletedTask;
    }

    private async Task RegisterModuleConfigAsync(IEnumerable<Assembly> assemblies, Container container, IModuleInfo moduleInfo)
    {
        foreach (var assembly in assemblies)
        {
            foreach (var module in assembly.Modules)
            {
                foreach (var type in module.GetTypes())
                {
                    var configAttr = type.GetCustomAttribute<SettingsAttribute>();

                    if (configAttr == null)
                    {
                        continue;
                    }

                    if (!type.IsInterface)
                    {
                        throw new ModuleServicesException($"Settings type {type} must be an interface.");
                    }

                    var store = await CreateModuleConfigStore(moduleInfo.Name, type);
                    var config = CreateConfigInstance(type, store);

                    if (config == null)
                    {
                        throw new InvalidOperationException("Failed to create module config instance.");
                    }
                
                    container.RegisterInstance(type, config);
                }
            }
        }
    }

    private async Task<IConfigStore> CreateModuleConfigStore(string name, Type configInterface)
    {
        var dbStore = new DatabaseStore(name, configInterface, _db);
        await dbStore.SetupDefaultSettingsAsync();

        return dbStore;
    }
    
    private object? CreateConfigInstance(Type configInterface, IConfigStore store)
    {
        var builder = ReflectionUtils.CreateGenericInstance(typeof(ConfigurationBuilder<>), configInterface);

        if (builder == null)
        {
            throw new InvalidOperationException("Failed to create module config builder.");
        }
        
        ReflectionUtils.CallMethod(builder, "UseConfigStore", store);
        return ReflectionUtils.CallMethod(builder, "Build");
    }

    private bool VerifyExternalModule(IExternalModuleInfo moduleInfo) =>
        !_config.Modules.RequireSignatureVerification || moduleInfo.ModuleFiles.All(file => file.VerifySignature());

    [MethodImpl(MethodImplOptions.NoInlining)]
    private (Type?, AssemblyLoadContext) CreateAssemblyLoadContext(Guid loadId, IExternalModuleInfo moduleInfo)
    {
        var asmLoadContext = new AssemblyLoadContext(loadId.ToString(), true);
        Type? mainClass = null;

        foreach (var dependency in moduleInfo.Dependencies)
        {
            var loadedDependency = GetLoadedDependency(dependency);

            foreach (var assembly in loadedDependency.Assemblies)
            {
                asmLoadContext.LoadFromAssemblyName(assembly.GetName());
            }
        }
        
        foreach (var asmFile in moduleInfo.AssemblyFiles)
        {
            var assembly = asmLoadContext.LoadFromAssemblyPath(asmFile.File.FullName);
            mainClass ??= assembly.AssemblyTypesWithAttribute<ModuleAttribute>().FirstOrDefault();
        }

        return (mainClass, asmLoadContext);
    }

    private IModuleLoadContext? GetLoadedDependency(IModuleDependency dependency)
    {
        var loadedDependency = _loadedModules
            .Values
            .FirstOrDefault(m => m.ModuleInfo.Name.Equals(dependency.Name));

        if (loadedDependency == null)
        {
            throw new InvalidOperationException(
                $"Tried to get module {dependency.Name} a loaded dependency, but it is not loaded.");
        }

        return loadedDependency;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private IEvoScModule CreateModuleInstance(Type mainClass, Container moduleServices) =>
        (IEvoScModule)ActivatorUtilities.CreateInstance(moduleServices, mainClass);

    private Dictionary<PipelineType, IActionPipeline> CreateDefaultPipelines() => 
        new()
        {
            {PipelineType.ChatRouter, new ActionPipeline()}, 
            {PipelineType.ControllerAction, new ActionPipeline()}
        };

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<IModuleLoadContext> CreateModuleLoadContextAsync(Guid loadId, Type mainClass, AssemblyLoadContext? asmLoadContext, IModuleInfo moduleInfo)
    {
        var assemblies = asmLoadContext?.Assemblies ?? new[] {mainClass.Assembly};
        
        var loadedDependencies = GetLoadedDependencies(moduleInfo);
        var moduleServices = _servicesManager.NewContainer(loadId, assemblies, loadedDependencies);
        moduleServices.RegisterInstance(moduleInfo);
        
        await RegisterModuleConfigAsync(assemblies, moduleServices, moduleInfo);
        var moduleInstance = CreateModuleInstance(mainClass, moduleServices);

        return new ModuleLoadContext
        {
            Instance = moduleInstance,
            Services = moduleServices,
            AsmLoadContext = asmLoadContext,
            LoadId = loadId,
            MainClass = mainClass,
            ModuleInfo = moduleInfo,
            Assemblies = assemblies,
            Pipelines = CreateDefaultPipelines(),
            Permissions = new List<IPermission>(),
            LoadedDependencies = loadedDependencies
        };
    }

    private List<Guid> GetLoadedDependencies(IModuleInfo moduleInfo)
    {
        var loadedDependencies = new List<Guid>();
        foreach (var dependency in moduleInfo.Dependencies)
        {
            var loadedDependency = GetLoadedDependency(dependency);

            loadedDependencies.Add(loadedDependency.LoadId);
        }

        return loadedDependencies;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task LoadInternalAsync(Guid loadId, IModuleInfo moduleInfo, Type mainClass, AssemblyLoadContext? asmLoadContext)
    {
        if (_moduleNameMap.ContainsKey(moduleInfo.Name))
        {
            _logger.LogError("A module with the identifier '{Name}' is already loaded. Will not load again",
                moduleInfo.Name);
            return;
        }
        
        _logger.LogDebug("Loading module '{Name}' as load ID '{LoadId}'", moduleInfo.Name, loadId);

        var loadContext = await CreateModuleLoadContextAsync(loadId, mainClass, asmLoadContext, moduleInfo);

        await RegisterMiddlewaresAsync(loadContext);
        await RegisterPermissionsAsync(loadContext);

        _loadedModules.Add(loadId, loadContext);
        _moduleNameMap[moduleInfo.Name] = loadId;

        _logger.LogDebug("External Module '{Name}' loaded with ID: {LoadId}", moduleInfo.Name, loadId);

        await InstallAsync(loadId);
        await EnableAsync(loadId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task EnableAsync(Guid loadId)
    {
        var moduleContext = GetModule(loadId);

        await EnableControllers(moduleContext);
        await EnableMiddlewares(moduleContext);
        await TryCallModuleEnable(moduleContext);

        moduleContext.SetEnabled(true);
        
        _logger.LogDebug("Module {Type}({Module}) was enabled", moduleContext.MainClass, loadId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task DisableAsync(Guid loadId)
    {
        var moduleContext = GetModule(loadId);

        await DisableControllers(moduleContext);
        await DisableMiddlewares(moduleContext);
        await TryCallModuleDisable(moduleContext);
        
        moduleContext.SetEnabled(false);
        
        _logger.LogDebug("Module {Type}({Module}) was disabled", moduleContext.MainClass, loadId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task LoadAsync(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"The module directory was not found at: {directory}");
        }

        var moduleInfo = ModuleInfoUtils.CreateFromDirectory(new DirectoryInfo(directory));
        return LoadAsync(moduleInfo);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task LoadAsync(IExternalModuleInfo moduleInfo)
    {
        if (!VerifyExternalModule(moduleInfo))
        {
            _logger.LogError("File signature verification failed for module {Name}. The module will not load",
                moduleInfo.Name);

            return Task.CompletedTask;
        }

        var loadId = Guid.NewGuid();
        var (mainClass, asmLoadContext) = CreateAssemblyLoadContext(loadId, moduleInfo);

        if (mainClass != null)
        {
            return LoadInternalAsync(loadId, moduleInfo, mainClass, asmLoadContext);
        }

        _logger.LogError("Failed to find the module main class for module {Name}. The module will not load",
            moduleInfo.Name);

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task LoadAsync(Assembly assembly)
    {
        var moduleInfo = ModuleInfoUtils.CreateFromAssembly(assembly);

        var loadId = Guid.NewGuid();
        var mainClass = moduleInfo.Assembly.AssemblyTypesWithAttribute<ModuleAttribute>().FirstOrDefault();
        
        if (mainClass != null)
        {
            return LoadInternalAsync(loadId, moduleInfo, mainClass, null);
        }
        
        _logger.LogError("Failed to find the module main class for module {Name}. The module will not load",
            moduleInfo.Name);

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task LoadAsync(IModuleCollection<IExternalModuleInfo> collection)
    {
        foreach (var module in collection)
        {
            await LoadAsync(module);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference> UnloadInternalAsync(Guid loadId)
    {
        var moduleContext = GetModule(loadId);

        if (moduleContext.ModuleInfo.IsInternal)
        {
            throw new EvoScModuleException($"Attempted to unload internal module '{loadId}' but this is not allowed");
        }

        foreach (var module in LoadedModules)
        {
            if (module.LoadedDependencies.Any(d => d == loadId))
            {
                await UnloadAsync(module.LoadId);
            }
        }

        if (moduleContext.IsEnabled)
        {
            await DisableAsync(loadId);
        }
        
        _loadedModules.Remove(loadId);

        var instanceWeakRef = new WeakReference(moduleContext.Instance);
        moduleContext.AsmLoadContext?.Unload();

        return instanceWeakRef;
    }
    
    [MethodImpl(MethodImplOptions.NoInlining)]
    public async Task UnloadAsync(Guid loadId)
    {
        var instanceWeakRef = await UnloadInternalAsync(loadId);
        
        for (var i = 0; instanceWeakRef.IsAlive && i < 10; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (instanceWeakRef.IsAlive)
        {
            _logger.LogWarning("Some references for module '{LoadId}' are still alive", loadId);
        }
        
        _logger.LogDebug("Module '{LoadId}' was unloaded", loadId);
    }
}

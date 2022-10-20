﻿using System.Diagnostics;
using System.Reflection;
using EvoSC.Common.Controllers;
using EvoSC.Common.Controllers.Attributes;
using EvoSC.Common.Interfaces;
using EvoSC.Common.Interfaces.Controllers;
using EvoSC.Common.Util;
using EvoSC.Modules.Attributes;
using EvoSC.Modules.Exceptions;
using EvoSC.Modules.Exceptions.ModuleServices;
using EvoSC.Modules.Extensions;
using EvoSC.Modules.Info;
using EvoSC.Modules.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace EvoSC.Modules;

public class ModuleManager : IModuleManager
{
    private readonly ILogger<ModuleManager> _logger;
    private readonly Container _services;
    private readonly IControllerManager _controllers;
    private readonly IModuleServicesManager _servicesManager;

    private Dictionary<Guid, IModuleLoadContext> _loadedModules = new();

    public ModuleManager(ILogger<ModuleManager> logger, Container services, IControllerManager controllers, IModuleServicesManager servicesManager)
    {
        _logger = logger;
        _services = services;
        _controllers = controllers;
        _servicesManager = servicesManager;
    }

    private async Task<Guid> LoadInternalModule(Type moduleClass, ModuleAttribute moduleInfo)
    {
        var loadId = Guid.NewGuid();
        var moduleServices = CreateServiceContainer(moduleClass.Assembly);
        var instance = (IEvoScModule)ActivatorUtilities.CreateInstance(moduleServices, moduleClass);

        var loadContext = new ModuleLoadContext
        {
            Instance = instance,
            LoadContext = null,
            LoadId = loadId,
            ModuleClass = moduleClass,
            ModuleInfo = moduleInfo,
            Assembly = moduleClass.Assembly,
            Services = moduleServices
        };
        
        _loadedModules.Add(loadId, loadContext);

        return loadId;
    }
    
    public async Task EnableModule(Guid loadId)
    {
        if (loadId == Guid.Empty || !_loadedModules.ContainsKey(loadId))
        {
            throw new EvoScModuleException($"Module with Id {loadId} does not exist.");
        }

        var moduleContext = _loadedModules[loadId];

        await EnableControllers(moduleContext);
        await EnableServices(moduleContext);
        await TryCallModuleEnable(moduleContext);
    }

    private async Task EnableServices(IModuleLoadContext moduleContext)
    {
        _servicesManager.AddContainer(moduleContext.LoadId, moduleContext.Services);
    }

    private Task TryCallModuleEnable(IModuleLoadContext moduleContext)
    {
        if (moduleContext.ModuleClass.IsAssignableTo(typeof(IToggleable)))
        {
            return ((IToggleable)moduleContext.Instance).Enable();
        }

        return Task.CompletedTask;
    }
    
    private Task EnableControllers(IModuleLoadContext moduleContext)
    {
        foreach (var module in moduleContext.Assembly.Modules)
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

        return Task.CompletedTask;
    }

    public Container CreateServiceContainer(Assembly assembly)
    {
        var container = new Container();
        container.Options.EnableAutoVerification = true;
        container.Options.SuppressLifestyleMismatchVerification = true;

        foreach (var module in assembly.Modules)
        {
            foreach (var type in module.GetTypes())
            {
                var serviceAttr = type.GetCustomAttribute<ServiceAttribute>();

                if (serviceAttr == null)
                {
                    continue;
                }

                var intf = type.GetInterfaces().FirstOrDefault();

                if (intf == null)
                {
                    throw new ModuleServicesException($"Service {type} must implement a custom interface.");
                }

                switch (serviceAttr.LifeStyle)
                {
                    case ServiceLifeStyle.Singleton:
                        //container.RegisterSingleton(intf, type);
                        container.Collection.Register(type);
                        break;
                    case ServiceLifeStyle.Transient:
                        container.Register(type);
                        break;
                }
            }
        }

        return container;
    }
    
    public async Task LoadModulesFromAssembly(Assembly assembly)
    {
        foreach (var asmModule in assembly.Modules)
        {
            foreach (var moduleType in asmModule.GetTypes())
            {
                if (!moduleType.IsEvoScModuleType())
                {
                    continue;
                }

                var moduleAttr = moduleType.GetModuleAttribute();
                var loadId = await LoadModule(moduleType, moduleAttr);

                await EnableModule(loadId);
            }
        }
    }

    private async Task<Guid> LoadModule(Type moduleType, ModuleAttribute moduleAttr)
    {
        var loadId = Guid.Empty;

        if (moduleAttr.IsInternal)
        {
            loadId = await LoadInternalModule(moduleType, moduleAttr);
        }
        
        _logger.LogDebug("Loaded module: {Type}", moduleType);
        
        return loadId;
    }
}

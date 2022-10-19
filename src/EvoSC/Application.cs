﻿
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using EvoSC.Common.Config;
using EvoSC.Common.Config.Models;
using EvoSC.Common.Controllers;
using EvoSC.Common.Database;
using EvoSC.Common.Events;
using EvoSC.Common.Interfaces;
using EvoSC.Common.Interfaces.Controllers;
using EvoSC.Common.Logging;
using EvoSC.Common.Remote;
using EvoSC.Common.Services;
using EvoSC.Modules;
using EvoSC.Modules.Extensions;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SimpleInjector;
using SimpleInjector.Lifestyles;

namespace EvoSC;

public class Application : IEvoSCApplication
{
    private readonly string[] _args;
    private Container _services;
    private bool _isDebug;
    private ILogger<Application> _logger;

    private readonly CancellationTokenSource _runningToken = new();
    
    public CancellationToken MainCancellationToken => _runningToken.Token;
    public Container Services => _services;

    public Application(string[] args)
    {
        _args = args;
        _isDebug = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") == "Development";
        
        ConfigureServiceContainer();
    }

    private void ConfigureServiceContainer()
    {
        _services = new Container();
        _services.Options.DefaultScopedLifestyle = new AsyncScopedLifestyle();
    }

    public async Task RunAsync()
    {
        var sw = new Stopwatch();
        sw.Start();

        SetupServices();
        MigrateDatabase();
        SetupControllerManager();
        await SetupModules();
        await StartBackgroundServices();
        
        sw.Stop();
        
        _logger.LogDebug("Startup time: {Time}ms", sw.ElapsedMilliseconds);

        // wait indefinitely
        WaitHandle.WaitAll(new[] {_runningToken.Token.WaitHandle});
    }

    public async Task ShutdownAsync()
    {
        var serverClient = _services.GetRequiredService<IServerClient>();
        await serverClient.StopAsync(_runningToken.Token);
        
        // cancel the token to stop the application itself
        _runningToken.Cancel();
    }

    private void SetupServices()
    {
        var config = _services.AddEvoScConfig();

        var dbConfig = config.Get<DatabaseConfig>(EvoScConfig.DatabaseConfigKey);
        _services.AddEvoScLogging(config.Get<LoggingConfig>(EvoScConfig.LoggingConfigKey));
        
        _services.AddEvoScMigrations();
        _services.AddEvoScDatabase(dbConfig);
        
        _services.AddGbxRemoteClient();
        _services.AddEvoScEvents();
        _services.AddEvoScModules();
        _services.AddEvoScControllers();
        _services.AddEvoScCommonServices();

        _services.RegisterInstance<IEvoSCApplication>(this);
        
        _logger = _services.GetRequiredService<ILogger<Application>>();
    }

    private void MigrateDatabase()
    {
        using var scope = new Scope(_services);
        var manager = scope.GetRequiredService<IMigrationManager>();
        
        // main migrations
        manager.MigrateFromAssembly(typeof(MigrationManager).Assembly);
        
        // internal modules
        manager.RunInternalModuleMigrations();
    }
    
    private void SetupControllerManager()
    {
        var controllers = _services.GetRequiredService<IControllerManager>();
        
        controllers.AddControllerActionRegistry(_services.GetRequiredService<IEventManager>());
    }
    
    private async Task SetupModules()
    {
        var modules = _services.GetRequiredService<IModuleManager>();

        modules.LoadInternalModules();
    }

    private async Task StartBackgroundServices()
    {
        _logger.LogDebug("Starting background services");
        
        // initialize event manager before anything else
        _services.GetRequiredService<IEventManager>();

        // connect to the dedicated server and setup callbacks
        var serverClient = _services.GetRequiredService<IServerClient>();
        var serverCallbacks = _services.GetRequiredService<IServerCallbackHandler>();
        await serverClient.StartAsync(_runningToken.Token);
    }
}

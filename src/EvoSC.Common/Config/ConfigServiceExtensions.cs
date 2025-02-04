﻿using Config.Net;
using EvoSC.Common.Config.Models;
using EvoSC.Common.Config.Stores;
using EvoSC.Common.Config.TypeParsers;
using EvoSC.Common.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using SimpleInjector;

namespace EvoSC.Common.Config;

public static class ConfigServiceExtensions
{
    private const string MainConfigFile = "config/main.toml";
    
    /// <summary>
    /// Set up and parse EvoSC's base configuration. This method also adds the configuration
    /// to the service container.
    /// </summary>
    /// <param name="services"></param>
    /// <returns></returns>
    public static IEvoScBaseConfig AddEvoScConfig(this Container services)
    {
        var baseConfig = new ConfigurationBuilder<IEvoScBaseConfig>()
            .UseTomlFile(MainConfigFile)
            .UseTypeParser(new TextColorTypeParser())
            .Build();

        services.RegisterInstance<IEvoScBaseConfig>(baseConfig);
        
        return baseConfig;
    }
}

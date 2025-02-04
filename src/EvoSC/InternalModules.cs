﻿using EvoSC.Common.Interfaces;
using EvoSC.Modules;
using EvoSC.Modules.Interfaces;
using EvoSC.Modules.Official.ExampleModule;
using EvoSC.Modules.Official.Maps;
using EvoSC.Modules.Official.Player;
using FluentMigrator.Runner.Exceptions;

namespace EvoSC;

public static class InternalModules
{
    public static readonly List<Type> Modules = new()
    {
        typeof(PlayerModule),
        typeof(ExampleModule),
        typeof(MapsModule)
    };

    /// <summary>
    /// Run any migrations from all the modules.
    /// </summary>
    /// <param name="migrations"></param>
    /// <exception cref="Exception"></exception>
    public static void RunInternalModuleMigrations(this IMigrationManager migrations)
    {
        foreach (var module in Modules)
        {
            try
            {
                migrations.MigrateFromAssembly(module.Assembly);
            }
            catch (MissingMigrationsException ex)
            {
                // ignore this as modules don't always have migrations, but we still try to find them
            }
        }
    }
    
    /// <summary>
    /// Load all internal modules.
    /// </summary>
    /// <param name="modules"></param>
    public static async Task LoadInternalModules(this IModuleManager modules)
    {
        foreach (var module in Modules)
        {
            await modules.LoadAsync(module.Assembly);
        }
    }
}

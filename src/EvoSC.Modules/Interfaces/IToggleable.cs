﻿namespace EvoSC.Modules.Interfaces;

/// <summary>
/// Defines a module as toggleable with custom enable/disable methods.
/// </summary>
public interface IToggleable
{
    /// <summary>
    /// Enable the module.
    /// </summary>
    /// <returns></returns>
    public Task EnableAsync();
    
    /// <summary>
    /// Disable the module.
    /// </summary>
    /// <returns></returns>
    public Task DisableAsync();
}

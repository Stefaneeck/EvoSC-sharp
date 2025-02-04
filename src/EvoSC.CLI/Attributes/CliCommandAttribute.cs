﻿namespace EvoSC.CLI.Attributes;

public class CliCommandAttribute : Attribute
{
    /// <summary>
    /// The name of the command.
    /// </summary>
    public required string Name { get; init; }
    /// <summary>
    /// Description of the command.
    /// </summary>
    public required string Description { get; init; }
}

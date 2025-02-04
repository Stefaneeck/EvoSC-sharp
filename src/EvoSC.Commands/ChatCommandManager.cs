﻿using System.Reflection;
using EvoSC.Commands.Attributes;
using EvoSC.Commands.Exceptions;
using EvoSC.Commands.Interfaces;
using EvoSC.Commands.Parser;
using EvoSC.Common.Events;
using EvoSC.Common.Events.Attributes;
using EvoSC.Common.Interfaces;
using EvoSC.Common.Interfaces.Controllers;
using EvoSC.Common.Interfaces.Parsing;
using EvoSC.Common.Remote;
using EvoSC.Common.TextParsing;
using EvoSC.Common.TextParsing.ValueReaders;
using EvoSC.Common.Util;
using GbxRemoteNet.Events;
using Microsoft.Extensions.Logging;
using StringReader = EvoSC.Common.TextParsing.ValueReaders.StringReader;

namespace EvoSC.Commands;

public class ChatCommandManager : IChatCommandManager
{
    public static readonly string CommandPrefix = "/";
    
    private readonly Dictionary<string, IChatCommand> _cmds = new();
    private readonly Dictionary<string, string> _aliasMap = new();
    private readonly Dictionary<Type, List<IChatCommand>> _controllerCommands = new();

    private readonly ILogger<ChatCommandManager> _logger;


    public ChatCommandManager(ILogger<ChatCommandManager> logger)
    {
        _logger = logger;
    }
    
    public void RegisterForController(Type controllerType)
    {
        if (!_controllerCommands.ContainsKey(controllerType))
        {
            _controllerCommands[controllerType] = new List<IChatCommand>();
        }
        
        var methods = controllerType.GetMethods(ReflectionUtils.InstanceMethods);

        foreach (var method in methods)
        {
            var cmdAttr = method.GetCustomAttribute<ChatCommandAttribute>();

            if (cmdAttr == null)
            {
                continue;
            }

            var aliasAttrs = method.GetCustomAttributes<CommandAliasAttribute>();

            var cmd = AddCommand(builder =>
            {
                builder
                    .WithName(cmdAttr.Name)
                    .WithDescription(cmdAttr.Description)
                    .WithPermission(cmdAttr.Permission)
                    .WithHandlerMethod(method)
                    .WithController(controllerType)
                    .UsePrefix(cmdAttr.UsePrefix);

                foreach (var alias in aliasAttrs)
                {
                    builder.AddAlias(new CommandAlias(alias));
                }
            });
            
            _controllerCommands[controllerType].Add(cmd);
        }
    }

    public void UnregisterForController(Type controllerType)
    {
        if (!_controllerCommands.ContainsKey(controllerType))
        {
            return;
        }
        
        foreach (var cmd in _controllerCommands[controllerType])
        {
            RemoveCommand(cmd);
        }
    }

    public void AddCommand(IChatCommand cmd)
    {
        if (_cmds.ContainsKey(cmd.Name))
        {
            throw new DuplicateChatCommandException(cmd.Name);
        }

        var lookupName = (cmd.UsePrefix ? CommandPrefix : "") + cmd.Name;
        
        _cmds[lookupName] = cmd;
        
        _logger.LogDebug("Registered command: {Name}", cmd.Name);
        
        MapCommandAliases(cmd);
    }

    private void MapCommandAliases(IChatCommand cmd)
    {
        if (cmd.Aliases != null)
        {
            var prefix = cmd.UsePrefix ? CommandPrefix : "";
            foreach (var (aliasName, alias) in cmd.Aliases)
            {
                if (_aliasMap.ContainsKey(aliasName))
                {
                    throw new DuplicateChatCommandException(alias.Name);
                }

                _aliasMap[aliasName] = prefix + cmd.Name;
                _logger.LogDebug("Registered command alias '{Alias}' for command '{Cmd}'", aliasName, cmd.Name);
            }
        }
    }

    public IChatCommand AddCommand(Action<ChatCommandBuilder> builderAction)
    {
        var builder = new ChatCommandBuilder();
        builderAction(builder);
        var cmd = builder.Build();
        
        AddCommand(cmd);
        
        return cmd;
    }

    public void RemoveCommand(IChatCommand cmd)
    {
        var cmdName = (cmd.UsePrefix ? CommandPrefix : "") + cmd.Name; 
        
        if (!_cmds.ContainsKey(cmdName))
        {
            throw new CommandNotFoundException(cmdName, false);
        }

        _cmds.Remove(cmdName);

        foreach (var alias in cmd.Aliases.Keys)
        {
            _aliasMap.Remove(alias);
        }

        _logger.LogDebug("Removed command: {Name}", cmdName);
    }

    public IChatCommand FindCommand(string alias) => FindCommand(alias, true);

    public IChatCommand FindCommand(string alias, bool withPrefix)
    {
        var lookupName = (withPrefix ? CommandPrefix : "") + alias;
        
        if (_cmds.ContainsKey(lookupName))
        {
            return _cmds[lookupName];
        }

        if (_aliasMap.ContainsKey(alias))
        {
            return _cmds[_aliasMap[alias]];
        }

        return null;
    }
}

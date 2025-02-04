﻿using EvoSC.Common.Interfaces.Models;
using GbxRemoteNet;

namespace EvoSC.Common.Interfaces;

public interface IServerClient
{
    /// <summary>
    /// The GBXRemote client instance.
    /// </summary>
    public GbxRemoteClient Remote { get; }
    /// <summary>
    /// Whether the client is connected to the remote XMLRPC server or not.
    /// </summary>
    public bool Connected { get; }

    /// <summary>
    /// Start the client and set up a connection.
    /// </summary>
    /// <param name="token">Cancellation token to cancel the startup.</param>
    /// <returns></returns>
    public Task StartAsync(CancellationToken token);
    
    /// <summary>
    /// Stop and disconnect from the server.
    /// </summary>
    /// <param name="token">Cancellation token to cancel the shutdown.</param>
    /// <returns></returns>
    public Task StopAsync(CancellationToken token);

    /// <summary>
    /// Send an info message to the chat.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <returns></returns>
    public Task InfoMessage(string text);
    
    /// <summary>
    /// Send an info message to a specific player.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <returns></returns>
    public Task InfoMessage(string text, IPlayer player);

    /// <summary>
    /// Send a success message to the chat.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <returns></returns>
    public Task SuccessMessage(string text);
    
    /// <summary>
    /// Send a success message to a specific player.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <returns></returns>
    public Task SuccessMessage(string text, IPlayer player);
    
    /// <summary>
    /// Send a warning message to the chat.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <returns></returns>
    public Task WarningMessage(string text);
    
    /// <summary>
    /// Send a warning message to a specific player.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <returns></returns>
    public Task WarningMessage(string text, IPlayer player);

    /// <summary>
    /// Send a error message to the chat.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <returns></returns>
    public Task ErrorMessage(string text);
    
    /// <summary>
    /// Send a error message to a specific player.
    /// </summary>
    /// <param name="text">Text to send.</param>
    /// <returns></returns>
    public Task ErrorMessage(string text, IPlayer player);
}

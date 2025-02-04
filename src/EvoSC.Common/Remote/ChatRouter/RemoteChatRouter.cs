﻿using EvoSC.Common.Events.CoreEvents;
using EvoSC.Common.Exceptions.PlayerExceptions;
using EvoSC.Common.Interfaces;
using EvoSC.Common.Interfaces.Middleware;
using EvoSC.Common.Interfaces.Services;
using EvoSC.Common.Middleware;
using EvoSC.Common.Util;
using EvoSC.Common.Util.ServerUtils;
using EvoSC.Common.Util.TextFormatting;
using GbxRemoteNet.Events;
using Microsoft.Extensions.Logging;

namespace EvoSC.Common.Remote.ChatRouter;

public class RemoteChatRouter : IRemoteChatRouter
{
    private readonly ILogger<RemoteChatRouter> _logger;
    private readonly IServerClient _server;
    private readonly IEventManager _events;
    private readonly IPlayerManagerService _players;
    private readonly IActionPipelineManager _pipelineManager;

    public RemoteChatRouter(ILogger<RemoteChatRouter> logger, IServerClient server, IEventManager events,
        IPlayerManagerService players, IActionPipelineManager pipelineManager)
    {
        _logger = logger;
        _server = server;
        _events = events;
        _players = players;
        _pipelineManager = pipelineManager;

        events.Subscribe(s => s
            .WithEvent(GbxRemoteEvent.PlayerChat)
            .WithInstance(this)
            .WithInstanceClass<ServerCallbackHandler>()
            .WithHandlerMethod<PlayerChatEventArgs>(HandlePlayerChatRouting)
            .AsAsync()
        );
    }

    private async Task HandlePlayerChatRouting(object sender, PlayerChatEventArgs e)
    {
        try
        {
            var accountId = PlayerUtils.ConvertLoginToAccountId(e.Login);
            var player = await _players.GetOnlinePlayerAsync(accountId);

            if (player.Flags.IsServer)
            {
                _logger.LogDebug("Chat message is from server, will ignore");
                return;
            }

            var pipelineChain = _pipelineManager.BuildChain(PipelineType.ChatRouter, async (context) =>
            {
                if (context is ChatRouterPipelineContext {ForwardMessage: true} chatContext)
                {
                    await _server.SendChatMessage(new TextFormatter()
                        .AddText("[")
                        .AddText(text => text.AsIsolated().AddText(player.NickName))
                        .AddText("] ")
                        .AddText(text => text.AsIsolated().AddText(chatContext.MessageText))
                    );
                }
            });

            await pipelineChain(new ChatRouterPipelineContext
            {
                ForwardMessage = true,
                Player = player,
                MessageText = e.Text
            });
            
            _logger.LogInformation("[{Name}]: {Msg}", player.StrippedNickName, e.Text);
        }
        catch (PlayerNotFoundException ex)
        {
            _logger.LogError("Failed to get online player: {PlayerId}", ex.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogCritical("Failed to route chat message: {Msg} | Stacktrace: {St}", ex.Message, ex.StackTrace);
            throw;
        }
    }
}

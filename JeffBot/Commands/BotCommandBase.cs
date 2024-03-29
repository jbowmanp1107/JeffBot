﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TwitchLib.Client.Exceptions;
using TwitchLib.Client.Interfaces;
using TwitchLib.Client.Models;
using TwitchLib.PubSub.Interfaces;

namespace JeffBot
{
    public abstract class BotCommandBase : IBotCommand
    {
        private DateTimeOffset _lastExecuted;
        private readonly Dictionary<string, DateTimeOffset> _usersLastExecuted;

        #region Logger
        public ILogger<JeffBot> Logger { get; set; }
        #endregion

        #region BotCommandSettings - IBotCommand Member
        public BotCommandSettings BotCommandSettings { get; set; }
        #endregion
        #region IsCommandEnabled - IBotCommand Member
        public bool IsCommandEnabled
        {
            get
            {
                return StreamerSettings.BotFeatures.Any(a => a.Name == BotCommandSettings.Name && a.IsEnabled);
            }
        }
        #endregion

        #region JeffBot
        public JeffBot JeffBot { get; set; } 
        #endregion
        #region TwitchApiClient - IBotCommand Member
        public ManagedTwitchApi TwitchApiClient { get; set; }
        #endregion
        #region TwitchChatClient - IBotCommand Member
        public ITwitchClient TwitchChatClient { get; set; }
        #endregion
        #region TwitchPubSubClient - IBotCommand Member
        public ITwitchPubSub TwitchPubSubClient { get; set; }
        #endregion
        #region StreamerSettings - IBotCommand Member
        public StreamerSettings StreamerSettings { get; set; }
        #endregion

        #region Constructor
        protected BotCommandBase(BotCommandSettings botCommandSettings, JeffBot jeffBot)
        {
            BotCommandSettings = botCommandSettings;
            JeffBot = jeffBot;
            TwitchApiClient = jeffBot.TwitchApiClient;
            TwitchChatClient = jeffBot.TwitchChatClient;
            TwitchPubSubClient = jeffBot.TwitchPubSubClient;
            StreamerSettings = jeffBot.StreamerSettings;
            Logger = jeffBot.Logger;
            _lastExecuted = DateTimeOffset.MinValue;
            _usersLastExecuted = new Dictionary<string, DateTimeOffset>();
        }
        #endregion

        #region CheckExecutionPermissionsAndExecuteCommand - IBotCommand Member
        public async Task CheckExecutionPermissionsAndExecuteCommand(ChatMessage chatMessage)
        {
            if (!IsCommandEnabled) return;
            var canExecuteCommand = await CommandIsAvailable();
            if (canExecuteCommand) canExecuteCommand = UserHasPermission(chatMessage);
            if (canExecuteCommand) canExecuteCommand = CheckCooldowns(chatMessage);
            if (canExecuteCommand) await ExecuteCommand(chatMessage);
        }
        #endregion

        #region ProcessMessage - IBotCommand Member - Abstract
        /// <summary>
        /// Attempts to process the message, if the message was a valid message for the command, return true, else return false.
        /// </summary>
        /// <param name="chatMessage"></param>
        /// <returns></returns>
        public abstract Task<bool> ProcessMessage(ChatMessage chatMessage);
        #endregion
        #region Initialize - IBotCommand Member - Abstract
        /// <summary>
        /// Any code required to initialize this command, such as specific API clients etc..
        /// </summary>
        public abstract void Initialize();
        #endregion

        #region UserHasPermission
        public virtual bool UserHasPermission(ChatMessage chatMessage)
        {
            var canExecuteCommand = false;
            switch (BotCommandSettings.PermissionLevel)
            {
                case FeaturePermissionLevel.Everyone:
                    canExecuteCommand = true;
                    break;
                case FeaturePermissionLevel.LoyalUser:
                    // TODO: Implement when points system is enabled.. (over X hours watched, can use command etc..)
                    break;
                case FeaturePermissionLevel.Subscriber:
                    if (chatMessage.IsSubscriber || chatMessage.IsVip || chatMessage.IsModerator || chatMessage.IsBroadcaster)
                        canExecuteCommand = true;
                    break;
                case FeaturePermissionLevel.Vip:
                    if (chatMessage.IsVip || chatMessage.IsModerator || chatMessage.IsBroadcaster) canExecuteCommand = true;
                    break;
                case FeaturePermissionLevel.Mod:
                    if (chatMessage.IsModerator || chatMessage.IsBroadcaster) canExecuteCommand = true;
                    break;
                case FeaturePermissionLevel.SuperMod:
                    // TODO: Implement when SuperMod (e.g. editor) functionality is implemented.
                    break;
                case FeaturePermissionLevel.Broadcaster:
                    if (chatMessage.IsBroadcaster) canExecuteCommand = true;
                    break;
            }

            return canExecuteCommand;
        }
        #endregion
        #region CommandIsAvailable
        public virtual async Task<bool> CommandIsAvailable()
        {
            switch (BotCommandSettings.CommandAvailability)
            {
                case CommandAvailability.Online:
                    return await IsStreamLive();
                case CommandAvailability.Offline:
                case CommandAvailability.Both:
                    return true;
                default:
                    return false;
            }
        }
        #endregion
        #region IsStreamLive
        public async Task<bool> IsStreamLive()
        {
            var isLive = await TwitchApiClient.ExecuteRequest(async api => await api.Helix.Streams.GetStreamsAsync(userIds: new List<string> { StreamerSettings.StreamerId }));
            return isLive.Streams.Any();
        }
        #endregion
        #region CheckCooldowns
        public virtual bool CheckCooldowns(ChatMessage chatMessage)
        {
            if (DateTimeOffset.UtcNow >= _lastExecuted.AddSeconds(BotCommandSettings.GlobalCooldown))
            {
                if (_usersLastExecuted.TryGetValue(chatMessage.Username.ToLower(), out DateTimeOffset value) && DateTimeOffset.UtcNow < value.AddSeconds(BotCommandSettings.UserCooldown))
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
            return true;
        }
        #endregion

        #region ExecuteCommand
        private async Task ExecuteCommand(ChatMessage chatMessage)
        {
            try
            {
                if (await ProcessMessage(chatMessage))
                {
                    _lastExecuted = DateTimeOffset.UtcNow;
                    _usersLastExecuted[chatMessage.Username.ToLower()] = DateTimeOffset.UtcNow;
                }
            }
            catch (BadStateException)
            {
                // TODO: Root cause fix this at some point.. Seems to be something weird going on in twitchlib.. sometimes it intiializes.. but doesn't join according to the library..
                TwitchChatClient.JoinChannel($"{StreamerSettings.StreamerName.ToLower()}");
            }
            catch (AggregateException aex)
            {
                if (aex.InnerException is BadStateException)
                {
                    // TODO: Root cause fix this at some point.. Seems to be something weird going on in twitchlib.. sometimes it intiializes.. but doesn't join according to the library..
                    TwitchChatClient.JoinChannel($"{StreamerSettings.StreamerName.ToLower()}");
                }
            }
            catch (Exception ex)
            {
                // In case we bubble up an exception..
                Logger.LogError(ex.ToString());
            }
        }
        #endregion
    }

    public abstract class BotCommandBase<T> : BotCommandBase where T : new()
    {
        #region BotCommandSettings
        public new BotCommandSettings<T> BotCommandSettings { get; set; }
        #endregion

        #region Constructor
        protected BotCommandBase(BotCommandSettings<T> botCommandSettings, JeffBot jeffBot) : base(botCommandSettings, jeffBot)
        {
            BotCommandSettings = botCommandSettings;
            BotCommandSettings.CustomSettings ??= new T();
        }
        #endregion
    }
}
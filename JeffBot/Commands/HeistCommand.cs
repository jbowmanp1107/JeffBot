using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TwitchLib.Api;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;

namespace JeffBot
{
    public class HeistCommand : BotCommandBase
    {
        #region Static Properties
        private static DateTimeOffset? LastHeistStart { get; set; }
        private static DateTimeOffset? LastHeistEnd { get; set; }
        #endregion
        #region Fields
        private CancellationTokenSource _cts;
        #endregion

        #region HeistSettings
        public HeistSettings HeistSettings { get; set; }
        #endregion
        #region PreviousHeistParticipants
        public List<HeistParticipant> PreviousHeistParticipants { get; set; } = new List<HeistParticipant>();
        #endregion
        #region HeistParticipants
        public List<HeistParticipant> HeistParticipants { get; set; } = new List<HeistParticipant>(); 
        #endregion
        #region HeistInProgress
        public bool HeistInProgress { get; set; } 
        #endregion
        #region StreamElementsClient
        public StreamElementsClient StreamElementsClient { get; set; }
        #endregion

        #region Constructor
        public HeistCommand(TwitchAPI twitchApiClient, TwitchClient twitchChatClient, TwitchPubSub twitchPubSub, StreamerSettings streamerSettings) : base(twitchApiClient, twitchChatClient, twitchPubSub, streamerSettings)
        {
            StreamElementsClient = new StreamElementsClient { ChannelId = streamerSettings.StreamElementsChannelId, JwtTokenString = streamerSettings.StreamElementsJwtToken };
            HeistSettings = new HeistSettings();
        }
        #endregion

        #region StartHeist
        public void StartHeist(string startingUser)
        {
            HeistInProgress = true;
            LastHeistStart = DateTimeOffset.Now;
            TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.OnFirstEntryMessage.Replace("{user}", startingUser));
            _cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                await Task.Delay(HeistSettings.StartDelay * 1000, _cts.Token);
                if (_cts.Token.IsCancellationRequested) return;
                await EndHeist();
            }, _cts.Token);
        }
        #endregion
        #region JoinHeist
        public async Task JoinHeist(string userName, bool isAll, int? points = null, bool resetUser = false)
        {
            var user = await StreamElementsClient.GetUser(userName);
            user.DisplayName = userName;
            if (!HeistInProgress && LastHeistEnd.HasValue &&
                LastHeistEnd.Value.AddSeconds(HeistSettings.Cooldown) > DateTimeOffset.Now)
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"{HeistSettings.WaitForCooldownMessage}: {Convert.ToInt32(HeistSettings.Cooldown - (DateTimeOffset.Now-LastHeistEnd.Value).TotalSeconds)} seconds remaining.");
            }
            if (LastHeistStart == null || !HeistInProgress && LastHeistEnd.HasValue && LastHeistEnd.Value.AddSeconds(HeistSettings.Cooldown) <= DateTimeOffset.Now)
            {
                if (!resetUser) if (await JoinAndSubtractPointsForUser(user, isAll, points)) StartHeist(userName);

            }
            else if (HeistInProgress)
            {
                await JoinAndSubtractPointsForUser(user, isAll, points, resetUser);
            }
        }
        #endregion
        #region EndHeist
        public async Task EndHeist(bool cancelHeist=false)
        {
            HeistInProgress = false;
            LastHeistEnd = DateTimeOffset.Now;

            if (cancelHeist)
            {
                try
                {
                    _cts.Cancel();
                    foreach (var participant in HeistParticipants)
                    {
                        await StreamElementsClient.AddOrRemovePointsFromUser(participant.User.Username, participant.Points);
                    }

                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.HeistCancelledMessage);
                    LastHeistEnd = DateTimeOffset.Now.AddSeconds(-300);
                }
                catch
                {
                    // Will only fail here if CancellationToken is already cancelled.. so whatever
                }
            }
            else
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.OnSuccessfulStartMessage);
                if (HeistParticipants.Count >= 8)
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.OnSuperHeistStartMessage);
                }
                var rnd = new Random();
                foreach (var participant in HeistParticipants)
                {
                    participant.WonHeist = rnd.Next(1, 100) < HeistSettings.ChanceToWinViewers;
                }

                if (HeistParticipants.All(a => a.WonHeist.HasValue && a.WonHeist.Value))
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistParticipants.Count > 1 ? HeistSettings.GroupOnAllWinMessage : HeistSettings.SoloOnWinMessage.Replace("{user}", HeistParticipants[0].User.DisplayName));
                }
                else if (HeistParticipants.All(a => a.WonHeist.HasValue && !a.WonHeist.Value))
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistParticipants.Count > 1 ? HeistSettings.GroupOnAllLoseMessage.Replace("{meatshields}", string.Join(',', HeistParticipants.Where(a => !a.WonHeist.Value).Select(a => $" riPepperonis {a.User.DisplayName}"))) : HeistSettings.SoloOnLossMessage.Replace("{user}", HeistParticipants[0].User.DisplayName));
                }
                else
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.GroupOnPartialWinMessage.Replace("{meatshields}", string.Join(',', HeistParticipants.Where(a => !a.WonHeist.Value).Select(a => $" riPepperonis {a.User.DisplayName}"))));
                }

                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), await DistributePointsAndGenerateResultString());
                if (HeistParticipants.Count(a => a.WonHeist is true) > 0 && HeistParticipants.Count(a => a.WonHeist is false) > 0)
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), "This heist isn't over yet! Heist winners can !rez <UserName> for a chance to rez someone who did not make it out alive, sacrificing half of their winnings, but stopping the fallen from losing their bet. Failing to successful rez will result in a loss of winnings.");
                }
            }
            PreviousHeistParticipants = HeistParticipants.ToList();
            HeistParticipants = new List<HeistParticipant>();

        }
        #endregion
        #region RezUser
        public async Task RezUser(string rezzingUser, string rezzedUser)
        {
            if (HeistInProgress) TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"Sorry {rezzingUser}, you cannot rez someone while a heist is still in progress!");
            var rezzingUserUser = PreviousHeistParticipants.FirstOrDefault(a => a.User.Username.ToLower() == rezzingUser.ToLower());
            if (rezzingUserUser == null)
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"Sorry {rezzingUser}, only people who participated in the last heist can rez!");
                return;
            }

            if (rezzingUserUser.WonHeist.Value == false)
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"Sorry {rezzingUser}, you cannot rez if you lost the last heist!");
                return;
            }

            var rezzedUserUser = PreviousHeistParticipants.FirstOrDefault(a => a.User.Username.ToLower() == rezzedUser.ToLower());
            if (rezzedUserUser == null)
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"Sorry {rezzingUser}, you cannot rez someone who did not participate in the last heist!");
                return;
            }

            if (rezzedUserUser.WonHeist.Value == true)
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"Sorry {rezzingUser}, you cannot rez someone who won the last heist!");
                return;
            }

            if (rezzedUserUser.WasRezzed.HasValue && rezzedUserUser.WasRezzed.Value == true)
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"Sorry {rezzingUser}, {rezzedUser} has already been rezzed.");
                return;
            }

            if (rezzingUserUser.UsedRez.HasValue && rezzingUserUser.UsedRez.Value == true)
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"Sorry {rezzingUser}, you can only rez one person per ");
                return;
            }

            if (rezzingUserUser.WonHeist.Value == true && rezzedUserUser.WonHeist.Value == false)
            {
                var rnd = new Random();
                if (rnd.Next(1, 100) < HeistSettings.ChanceToWinViewers)
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"{rezzingUser} swooped in and sacrificed half of their heist winnings ({rezzingUserUser.Points / 2}) to bring back {rezzedUser} from the dead and recover their original bet ({rezzedUserUser.Points})!");
                    await StreamElementsClient.AddOrRemovePointsFromUser(rezzingUserUser.User.Username, (rezzingUserUser.Points / 2) * -1);
                    await StreamElementsClient.AddOrRemovePointsFromUser(rezzedUserUser.User.Username, rezzedUserUser.Points);
                    rezzedUserUser.WasRezzed = true;
                }
                else
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), $"{rezzingUser} got stunned while trying to rez {rezzedUser} and lost all there winnings({rezzingUserUser.Points})!");
                    await StreamElementsClient.AddOrRemovePointsFromUser(rezzingUserUser.User.Username, (rezzingUserUser.Points) * -1);
                }
                rezzingUserUser.UsedRez = true;
            }
        }
        #endregion

        #region JoinAndSubtractPointsForUser
        private async Task<bool> JoinAndSubtractPointsForUser(StreamElementsUser user, bool isAll, int? points = null, bool resetUser = false)
        {
            if (resetUser)
            {
                if (HeistParticipants.Any(a => a.User.Username == user.Username))
                {
                    var me = HeistParticipants.FirstOrDefault(a => a.User.Username == user.Username);
                    if (me == null)
                    {
                        TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.HeistResetMeNotJoinedMessage.Replace("{user}", user.DisplayName));
                        return false;
                    }
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.HeistResetMeMessage.Replace("{user}", user.DisplayName));
                    await StreamElementsClient.AddOrRemovePointsFromUser(me.User.Username, me.Points);
                    HeistParticipants.Remove(me);
                    if (!HeistParticipants.Any()) await this.EndHeist(true);
                }
                return false;
            }
            if (HeistParticipants.Any(a => a.User.Username == user.Username))
            {
                TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.UserAlreadyJoinedMessage.Replace("{user}", user.DisplayName));
                return false;
            }
            else
            {
                if (points.HasValue && points.Value > user.Points)
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.UserNotEnoughPointsMessage.Replace("{user}", user.DisplayName).Replace("{points}", user.Points.ToString()));
                    return false;
                }

                if (points.HasValue && points.Value > HeistSettings.MaxAmount)
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.UserOverMaxPointsMessage.Replace("{user}", user.DisplayName).Replace("{maxamount}", HeistSettings.MaxAmount.ToString()));
                    return false;
                }

                if (points.HasValue && points.Value < HeistSettings.MinEntries)
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.UserUnderMinPointsMessage.Replace("{user}", user.DisplayName).Replace("{minentries}", HeistSettings.MinEntries.ToString()));
                    return false;
                }

                var participant = new HeistParticipant { User = user };
                if (points.HasValue)
                {
                    await StreamElementsClient.AddOrRemovePointsFromUser(user.Username, -points.Value);
                    participant.Points = points.Value;
                }

                if (isAll)
                {
                    if (user.Points >= HeistSettings.MaxAmount)
                    {
                        await StreamElementsClient.AddOrRemovePointsFromUser(user.Username, -HeistSettings.MaxAmount);
                        participant.Points = HeistSettings.MaxAmount;
                    }
                    else
                    {
                        if (user.Points > 0)
                        {
                            await StreamElementsClient.AddOrRemovePointsFromUser(user.Username, -(int)user.Points);
                            participant.Points = (int)user.Points;
                        }
                        else
                        {
                            await StreamElementsClient.AddOrRemovePointsFromUser(user.Username, -1);
                            participant.Points = 1;
                        }
                    }
                }

                if (HeistParticipants.Count > 0)
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), HeistSettings.OnEntryMessage.Replace("{user}", user.DisplayName));
                }

                if (HeistParticipants.Count == 8)
                {
                    TwitchChatClient.SendMessage(StreamerSettings.StreamerName.ToLower(), ".announce Eight people have joined the heist! Winners of this heist will receive double points!");
                }

                HeistParticipants.Add(participant);
                return true;
            }
        } 
        #endregion
        #region DistributePointsAndGenerateResultString
        private async Task<string> DistributePointsAndGenerateResultString()
        {
            string resultString = "";
            foreach (var winner in HeistParticipants.Where(a => a.WonHeist.HasValue && a.WonHeist.Value))
            {
                var points = winner.Points * 2;
                if (HeistParticipants.Count >= 8)
                {
                    points = points * 2;
                }
                await StreamElementsClient.AddOrRemovePointsFromUser(winner.User.Username, points);
                if (string.IsNullOrWhiteSpace(resultString))
                {
                    resultString = $"Result: {winner.User.DisplayName} ({points})";
                }
                else
                {
                    resultString = resultString + $", {winner.User.DisplayName} ({points})";
                }
            }

            return resultString;
        }
        #endregion

        #region ProcessMessage - IBotCommand Member
        public override void ProcessMessage(ChatMessage chatMessage)
        {
            if (StreamerSettings.BotFeatures.Contains(BotFeatures.Heist))
            {
                #region Heist Number
                var isHeistMessage = Regex.Match(chatMessage.Message.ToLower(), @"^!heist \d+$");
                if (isHeistMessage.Captures.Count > 0)
                {
                    var number = Regex.Match(chatMessage.Message, @"\d+$");
                    if (number.Captures.Count > 0)
                    {
                        this.JoinHeist(chatMessage.DisplayName, false, Convert.ToInt32(number.Captures[0].Value)).Wait();
                    }
                }
                #endregion

                #region Heist All
                var isHeistAllMessage = Regex.Match(chatMessage.Message.ToLower(), @"^!heist all$");
                if (isHeistAllMessage.Captures.Count > 0)
                {
                    JoinHeist(chatMessage.DisplayName, true).Wait();
                }
                #endregion

                #region Heist Cancel
                var isHeistCancelMessage = Regex.Match(chatMessage.Message.ToLower(), @"^!heist cancel$");
                if (isHeistCancelMessage.Captures.Count > 0)
                {
                    if (chatMessage.IsBroadcaster || chatMessage.IsModerator)
                    {
                        EndHeist(true).Wait();
                    }
                }
                #endregion

                #region Heist Reset Me
                var isHeistResetMeMessage = Regex.Match(chatMessage.Message.ToLower(), @"^!heist undo$");
                if (isHeistResetMeMessage.Captures.Count > 0)
                {
                    JoinHeist(chatMessage.DisplayName, false, null, true).Wait();
                }
                #endregion

                #region Heist Rez
                var isHeistRezMessage = Regex.Match(chatMessage.Message.ToLower(), @"^!rez \S+$");
                if (isHeistRezMessage.Captures.Count > 0)
                {
                    var personToRez = chatMessage.Message.Replace("!rez ", string.Empty);
                    if (personToRez.StartsWith("@"))
                    {
                        personToRez = personToRez.Remove(0, 1);
                    }
                    RezUser(chatMessage.DisplayName, personToRez).Wait();
                }
                #endregion
            }
        }
        #endregion
        #region Initialize - IBotCommand Method
        public override void Initialize()
        { } 
        #endregion
    }
}
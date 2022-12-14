using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Clips.CreateClip;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.PubSub;

namespace JeffBot
{
    public class AdvancedClipCommand : BotCommandBase
    {
        #region NoobHunterFormUrl
        public string NoobHunterFormUrl { get; set; } = "http://bit.ly/NHClips";
        #endregion
        #region MostRecentClips
        public Dictionary<string, (string url, DateTime dateTime)> MostRecentClips { get; set; } = new Dictionary<string, (string url, DateTime dateTime)> ();
        #endregion

        #region Constructor
        public AdvancedClipCommand(TwitchAPI twitchApiClient, TwitchClient twitchChatClient, TwitchPubSub twitchPubSub, StreamerSettings streamerSettings) : base(twitchApiClient, twitchChatClient, twitchPubSub, streamerSettings)
        {
        }
        #endregion

        #region CreateTwitchClip
        public void CreateTwitchClip(ChatMessage chatMessage, bool canPerformAdvancedClip)
        {
            CreatedClipResponse clip = null;
            try
            {
                //if (chatMessage.IsVip || chatMessage.IsModerator || chatMessage.IsBroadcaster || chatMessage.IsSubscriber)
                //{
                    var isLive = TwitchApiClient.Helix.Streams.GetStreamsAsync(userIds: new List<string> { StreamerSettings.StreamerId }).Result;
                    if (!isLive.Streams.Any())
                    {
                        TwitchChatClient.SendMessage(chatMessage.Channel, $"Cannot create clip for an offline stream.");
                        return;
                    }
                    clip = TwitchApiClient.Helix.Clips.CreateClipAsync(StreamerSettings.StreamerId).Result;

                    if (clip != null && clip.CreatedClips.Any())
                    {
                        TwitchChatClient.SendMessage(chatMessage.Channel, $"Clip created successfully {clip.CreatedClips[0].EditUrl.Replace("/edit", string.Empty)}");
                        MostRecentClips[chatMessage.Username] = (clip.CreatedClips[0].EditUrl.Replace("/edit", string.Empty), DateTime.UtcNow);
                        if (canPerformAdvancedClip) TwitchChatClient.SendMessage(chatMessage.Channel, $"@{chatMessage.DisplayName} you can submit this clip to NoobHunter for consideration by typing \"!clip noobhunter\" in chat.");

                    }
                    else
                    {
                        TwitchChatClient.SendMessage(chatMessage.Channel, $"Stream NOT successfully clipped.");
                    }
                //}
                //else
                //{
                //    TwitchChatClient.SendMessage(chatMessage.Channel, $"Sorry {chatMessage.DisplayName}, only {chatMessage.Channel}, Subscribers, VIPS, and Moderators can clip the stream from chat.");
                //}
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Source == "Newtonsoft.Json")
                {
                    if (clip != null && clip.CreatedClips.Any())
                    {
                        TwitchChatClient.SendMessage(chatMessage.Channel, $"Stream successfully clipped: ");
                        TwitchChatClient.SendMessage(chatMessage.Channel, $"Clip created successfully {clip.CreatedClips[0].EditUrl.Replace("/edit", string.Empty)}");
                        MostRecentClips[chatMessage.Username] = (clip.CreatedClips[0].EditUrl.Replace("/edit", string.Empty), DateTime.UtcNow);
                        if (canPerformAdvancedClip) TwitchChatClient.SendMessage(chatMessage.Channel, $"@{chatMessage.DisplayName} you can submit this clip to NoobHunter for consideration by typing \"!clip noobhunter\" in chat.");
                    }
                    else
                    {
                        TwitchChatClient.SendMessage(chatMessage.Channel, $"Stream NOT successfully clipped.");
                    }
                }
                else
                {
                    TwitchChatClient.SendMessage(chatMessage.Channel, "Stream was NOT successfully clipped.. Someone tell Jeff..");
                }
            }
        }
        #endregion
        #region ValidateAndPostToNoobHuner
        public void ValidateAndPostToNoobHuner(ChatMessage chatMessage)
        {
            string url = string.Empty;

            if (MostRecentClips.TryGetValue(chatMessage.Username, out (string url, DateTime dateTime) clip))
            {
                url = clip.url;
            }
            else if (chatMessage.IsModerator)
            {
                if (MostRecentClips.Count > 0)
                {
                    url = MostRecentClips.FirstOrDefault(a => a.Value.dateTime == MostRecentClips.Max(b => b.Value.dateTime)).Value.url;
                }
            }
            else
            {
                TwitchChatClient.SendMessage(chatMessage.Channel, $"Sorry {chatMessage.DisplayName}, there are currently no clips you can submit to NoobHunter, please use !clip and then try again.");
            }
            if (url != string.Empty)
            {
                var result = FillOutNoobHunterFormAndSubmit(url);
                if (result.success)
                {
                    MostRecentClips.Remove(chatMessage.Username);
                    TwitchChatClient.SendMessage(chatMessage.Channel, $"{chatMessage.DisplayName}, your clip has been successfully submitted to NoobHunter!");
                }
                else
                {
                    TwitchChatClient.SendMessage(chatMessage.Channel, $"An error occurred submitting your clip to NoobHunter, you can try again, or just yell at Jeff to fix it.");
                }
            }
        } 
        #endregion

        #region FillOutNoobHunterFormAndSubmit
        private (bool success, string message) FillOutNoobHunterFormAndSubmit(string url)
        {
            ChromeDriver driver = null;
            try
            {
                var chromeOptions = new ChromeOptions();
                chromeOptions.AddArguments("headless");
                driver = new ChromeDriver(chromeOptions);
                driver.Navigate().GoToUrl(NoobHunterFormUrl);
                var firstQuestion = WaitAndFindElementByXpath(driver, "//div[contains(@data-params, 'Clip Link')]");
                var firstQuestionInput = firstQuestion.FindElement(By.TagName("textarea"));
                firstQuestionInput.SendKeys(url);
                var secondQuestion = WaitAndFindElementByXpath(driver, "//div[contains(@data-params, 'Featured Name')]");
                var secondQuestionInput = secondQuestion.FindElement(By.TagName("input"));
                secondQuestionInput.SendKeys(StreamerSettings.StreamerName);
                var submitButton = WaitAndFindElementByXpath(driver, "//span[text()='Submit']");
                submitButton.Click();

                try
                {
                    var waitForSubmit = new WebDriverWait(driver, TimeSpan.FromSeconds(15)).Until(a => a.FindElement(By.PartialLinkText("Submit another response")));
                    return (true, "lol");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    return (false, ex.Message);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return (false, ex.Message);
            }
            finally
            {
                if (driver != null)
                {
                    try
                    {
                        Console.WriteLine("Closing Chrome Driver");
                        driver.Close();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        // Swallow
                    }
                }
            }
        }
        #endregion
        #region WaitAndFindElementByXpath
        private IWebElement WaitAndFindElementByXpath(IWebDriver driver, string xpath)
        {
            return new WebDriverWait(driver, TimeSpan.FromSeconds(15)).Until(a => a.FindElement(By.XPath(xpath)));
        }
        #endregion

        #region ProcessMessage - IBotCommand Member
        public override void ProcessMessage(ChatMessage chatMessage)
        {
            if (StreamerSettings.BotFeatures.Contains(BotFeatures.Clip))
            {
                #region Clip
                var isClipMessage = Regex.Match(chatMessage.Message.ToLower(), @"^!clip$");
                if (isClipMessage.Captures.Count > 0)
                {
                    CreateTwitchClip(chatMessage, StreamerSettings.BotFeatures.Contains(BotFeatures.AdvancedClip));
                }
                #endregion
            }

            if (StreamerSettings.BotFeatures.Contains(BotFeatures.AdvancedClip))
            {
                #region Clip Noobhunter
                var isPostNoobHunter = Regex.Match(chatMessage.Message.ToLower(), @"^!clip noobhunter$");
                if (isPostNoobHunter.Captures.Count > 0)
                {
                    ValidateAndPostToNoobHuner(chatMessage);
                }
                #endregion
            }
        }
        #endregion
        #region Initialize - IBotCommand Member
        public override void Initialize()
        {
        }
        #endregion
    }
}
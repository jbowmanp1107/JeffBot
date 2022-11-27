﻿using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using TwitchLib.Api;
using TwitchLib.Api.Helix.Models.Clips.CreateClip;
using TwitchLib.Client;
using TwitchLib.Client.Events;

namespace JeffBot
{
    public class AdvancedClipper
    {
        #region StreamerSettings
        public StreamerSettings StreamerSettings { get; set; }
        #endregion
        #region TwitchApi
        public TwitchAPI TwitchApi { get; set; }
        #endregion
        #region TwitchChatClient
        public TwitchClient TwitchChatClient { get; set; }
        #endregion
        #region NoobHunterFormUrl
        public string NoobHunterFormUrl { get; set; } = "http://bit.ly/NHClips";
        #endregion
        #region MostRecentClips
        public Dictionary<string, (string url, DateTime dateTime)> MostRecentClips { get; set; } = new Dictionary<string, (string url, DateTime dateTime)> (); 
        #endregion

        #region CreateTwitchClip
        public void CreateTwitchClip(OnMessageReceivedArgs e, bool canPerformAdvancedClip)
        {
            CreatedClipResponse clip = null;
            try
            {
                //if (e.ChatMessage.IsVip || e.ChatMessage.IsModerator || e.ChatMessage.IsBroadcaster || e.ChatMessage.IsSubscriber)
                //{
                    var isLive = TwitchApi.Helix.Streams.GetStreamsAsync(userIds: new List<string> { StreamerSettings.StreamerId }).Result;
                    if (!isLive.Streams.Any())
                    {
                        TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"Cannot create clip for an offline stream.");
                        return;
                    }
                    clip = TwitchApi.Helix.Clips.CreateClipAsync(StreamerSettings.StreamerId).Result;

                    if (clip != null && clip.CreatedClips.Any())
                    {
                        TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"Clip created successfully {clip.CreatedClips[0].EditUrl.Replace("/edit", string.Empty)}");
                        MostRecentClips[e.ChatMessage.Username] = (clip.CreatedClips[0].EditUrl.Replace("/edit", string.Empty), DateTime.UtcNow);
                        if (canPerformAdvancedClip) TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"@{e.ChatMessage.DisplayName} you can submit this clip to NoobHunter for consideration by typing \"!clip noobhunter\" in chat.");

                    }
                    else
                    {
                        TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"Stream NOT successfully clipped.");
                    }
                //}
                //else
                //{
                //    TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"Sorry {e.ChatMessage.DisplayName}, only {e.ChatMessage.Channel}, Subscribers, VIPS, and Moderators can clip the stream from chat.");
                //}
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null && ex.InnerException.Source == "Newtonsoft.Json")
                {
                    if (clip != null && clip.CreatedClips.Any())
                    {
                        TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"Stream successfully clipped: ");
                        TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"Clip created successfully {clip.CreatedClips[0].EditUrl.Replace("/edit", string.Empty)}");
                        MostRecentClips[e.ChatMessage.Username] = (clip.CreatedClips[0].EditUrl.Replace("/edit", string.Empty), DateTime.UtcNow);
                        if (canPerformAdvancedClip) TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"@{e.ChatMessage.DisplayName} you can submit this clip to NoobHunter for consideration by typing \"!clip noobhunter\" in chat.");
                    }
                    else
                    {
                        TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"Stream NOT successfully clipped.");
                    }
                }
                else
                {
                    TwitchChatClient.SendMessage(e.ChatMessage.Channel, "Stream was NOT successfully clipped.. Someone tell Jeff..");
                }
            }
        }
        #endregion
        #region ValidateAndPostToNoobHuner
        public void ValidateAndPostToNoobHuner(OnMessageReceivedArgs e)
        {
            string url = string.Empty;

            if (MostRecentClips.TryGetValue(e.ChatMessage.Username, out (string url, DateTime dateTime) clip))
            {
                url = clip.url;
            }
            else if (e.ChatMessage.IsModerator)
            {
                if (MostRecentClips.Count > 0)
                {
                    url = MostRecentClips.FirstOrDefault(a => a.Value.dateTime == MostRecentClips.Max(b => b.Value.dateTime)).Value.url;
                }
            }
            else
            {
                TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"Sorry {e.ChatMessage.DisplayName}, there are currently no clips you can submit to NoobHunter, please use !clip and then try again.");
            }
            if (url != string.Empty)
            {
                var result = FillOutNoobHunterFormAndSubmit(url);
                if (result.success)
                {
                    MostRecentClips.Remove(e.ChatMessage.Username);
                    TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"{e.ChatMessage.DisplayName}, your clip has been successfully submitted to NoobHunter!");
                }
                else
                {
                    TwitchChatClient.SendMessage(e.ChatMessage.Channel, $"An error occurred submitting your clip to NoobHunter, you can try again, or just yell at Jeff to fix it.");
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
    }
}
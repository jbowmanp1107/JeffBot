﻿using System.Threading.Tasks;
using TwitchLib.Client.Models;

namespace JeffBot
{
    public interface ICommandOutputVariable
    {
        #region Keyword
        public string Keyword { get; set; }
        #endregion
        #region Description
        string Description { get; set; }
        #endregion

        #region ProcessVariable
        Task<string> ProcessVariable(string variable, ChatMessage chatMessage);
        #endregion
    }
}
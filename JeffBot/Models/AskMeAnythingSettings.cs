﻿namespace JeffBot
{
    public class AskMeAnythingSettings
    {
        #region AdditionalAIPrompt
        public string AdditionalAIPrompt { get; set; }
        #endregion
        #region ShouldReactToFirstTimeChatters
        public bool ShouldReactToFirstTimeChatters { get; set; }
        #endregion
        #region ShouldReactToFollows
        public bool ShouldReactToFollows { get; set; }
        #endregion
        #region ShouldReactToUserSubs
        public bool ShouldReactToUserSubs { get; set; }
        #endregion
        #region ShouldReactToGiftSubs
        public bool ShouldReactToGiftSubs { get; set; }
        #endregion
        #region ShouldReactToBits
        public bool ShouldReactToBits { get; set; }
        #endregion
        #region ShouldReactToRaids
        public bool ShouldReactToRaids { get; set; }
        #endregion
    }
}
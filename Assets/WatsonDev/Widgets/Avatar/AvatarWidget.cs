﻿/**
* Copyright 2015 IBM Corp. All Rights Reserved.
*
* Licensed under the Apache License, Version 2.0 (the "License");
* you may not use this file except in compliance with the License.
* You may obtain a copy of the License at
*
*      http://www.apache.org/licenses/LICENSE-2.0
*
* Unless required by applicable law or agreed to in writing, software
* distributed under the License is distributed on an "AS IS" BASIS,
* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
* See the License for the specific language governing permissions and
* limitations under the License.
*
* @author Dogukan Erenel (derenel@us.ibm.com)
* @author Richard Lyle (rolyle@us.ibm.com)
*/


using IBM.Watson.Logging;
using IBM.Watson.Utilities;
using IBM.Watson.Data;
using IBM.Watson.Data.XRAY;
using IBM.Watson.Services.v1;
using UnityEngine;
using System;
using IBM.Watson.Debug;
using IBM.Watson.Widgets.Question;

#pragma warning disable 414

namespace IBM.Watson.Widgets.Avatar
{
    /// <summary>
    /// Avatar of Watson 
    /// </summary>
    public class AvatarWidget : Widget
    {
        #region Public Types
        /// <summary>
        /// State of the Avatar in terms of its behavior.
        /// </summary>
        public enum AvatarState
        {
            /// <summary>
            /// The initial mode (before app start) - not used for anything
            /// </summary>
            NONE = -1,
            /// <summary>
            /// Connecting - initial state
            /// </summary>
            CONNECTING,
            /// <summary>
            /// Connected - Sleeping but continuously listening to wake up
            /// </summary>
            SLEEPING_LISTENING,
            /// <summary>
            /// Connected - Listening continuously to understand the input
            /// </summary>
            LISTENING,
            /// <summary>
            /// Connected - After some input it is the time for thinking the understand the input
            /// </summary>
            THINKING,
            /// <summary>
            /// Connected - After semantically understanding, Watson is responding. 
            /// If Watson didn't understand the input, then it goes to listening mode while giving some response about not understood input.
            /// </summary>
            ANSWERING,
			/// <summary>
			/// Didn't understand - This happens in one frame to show that that dialog didn't understand by avatar
			/// </summary>
			DONT_UNDERSTAND,
            /// <summary>
            /// Some type of error occured that is keeping the avatar from working.
            /// </summary>
            ERROR
        };

        /// <summary>
        /// Avatar Mood which effects various things in the application like Animation speed, coloring etc.
        /// </summary>
        public enum MoodType
        {
            /// <summary>
            /// The initial mode (before app start) 
            /// </summary>
            NONE = -1,
            /// <summary>
			/// Connecting / Disconnected - Waiting to be waken-up ( initial state )
			/// </summary>
            SLEEPING,
            /// <summary>
            /// Connected - After wake up - waits a mood change
            /// </summary>
            IDLE,
            /// <summary>
            /// Connected - After wake up - set interested mood
            /// </summary>
            INTERESTED,
            /// <summary>
            /// Connected - After wake up - set urgent mood
            /// </summary>
            URGENT,
            /// <summary>
            /// Connected - After wake up - set upset mood
            /// </summary>
            UPSET,
            /// <summary>
            /// Connected - After wake up - set shy mood
            /// </summary>
            SHY
        }

        #endregion

        #region Private Data
        private XRAY m_XRAY = new XRAY();                      // XRAY service
        private Dialog m_Dialog = new Dialog();             // Dialog service

        private AvatarState m_State = AvatarState.NONE;
        private AvatarState m_PreviousListeningMode = AvatarState.SLEEPING_LISTENING;
        private ClassifyResult m_ClassifyResult = null;

        private SpeechResultList m_SpeechResult = null;
        private Questions m_QuestionResult = null;
        private Answers m_AnswerResult = null;
        [SerializeField]
        private int m_MaxAnswerLength = 255;
        private string m_LastAnswer = string.Empty;
        private ParseData m_ParseData = null;
        private QuestionWidget m_FocusQuestion = null;
        private bool m_GettingAnswers = false;
        private bool m_GettingParse = false;

        [SerializeField]
        private string [] m_HelloPhrases = new string[] { "Hello", "Yo", "Whats up", "Hey you", "Hows it hanging" };
        [SerializeField]
        private string [] m_GoodbyePhrases = new string[] { "Goodbye", "Laters", "Later Taters", "See ya", "Bye Bye", "Take Care", "Peace Out" };
        [SerializeField]
        private string [] m_FailurePhrases = new string[] {  "I'm sorry, but I didn't understand your question.", "Huh", "I didn't catch that", "What did you say again?", "Come again", "What was that", "pardon" };
        [SerializeField]
        private string [] m_ErrorPhrases = new string[] {  "Oh bugger, something has gone wrong.", "Oh Shoot", "Oh no", "Oh Fudge",  };
        [SerializeField]
        private string m_Pipeline = "thunderstone";
        [SerializeField]
        private Input m_levelInput = new Input("Level", typeof(FloatData), "OnLevelInput");
        [SerializeField]
        private Input m_SpeakingInput = new Input( "Speaking", typeof(SpeakingStateData), "OnSpeaking" );
        [SerializeField]
        private Output m_TextOutput = new Output(typeof(TextData));
        [SerializeField]
        private GameObject m_QuestionPrefab = null;
        [SerializeField]
        private string m_DialogName = "xray";
        private string m_DialogId = null;
        private int m_DialogClientId = 0;
        private int m_DialogConversationId = 0;
        [SerializeField, Tooltip("If disconnected, how many seconds until we try to restart the avatar.")]
        private float m_RestartInterval = 30.0f;
        #endregion

        #region Public Properties
        /// <summary>
        /// Access the contained XRAY service object.
        /// </summary>
        public XRAY XRAY { get { return m_XRAY; } }
        /// <summary>
        /// What is the current state of this avatar.
        /// </summary>
        public AvatarState State
        {
            get { return m_State; }
            private set
            {
                if(m_State == AvatarState.LISTENING || m_State == AvatarState.SLEEPING_LISTENING)
                    m_PreviousListeningMode = m_State;

                if (m_State != value)
                {
                    m_State = value;
                    EventManager.Instance.SendEvent(Constants.Event.ON_CHANGE_AVATAR_STATE_FINISH, this, value);

					// if we went into an error state, automatically try to reconnect after a timeout..
                    if (m_State == AvatarState.ERROR)
                    {
                        if ( m_FocusQuestion != null )
                            m_FocusQuestion.OnLeaveTheSceneAndDestroy();

                        Invoke("StartAvatar", m_RestartInterval);
                        m_TextOutput.SendData( new TextData( PickRandomString( m_ErrorPhrases ) ) );
                    }
                }

				if(m_State == AvatarState.CONNECTING || m_State == AvatarState.ERROR || m_State == AvatarState.SLEEPING_LISTENING)
					Mood = MoodType.SLEEPING;
            }
        }


        #endregion

        #region Widget Interface
        protected override string GetName()
        {
            return "Avatar";
        }
        #endregion

        #region Pebble Manager for Visualization
        private PebbleManager m_pebbleManager;

        /// <summary>
        /// Gets the pebble manager. Sound Visualization on the avatar. 
        /// </summary>
        /// <value>The pebble manager.</value>
        public PebbleManager pebbleManager
        {
            get
            {
                if (m_pebbleManager == null)
                    m_pebbleManager = GetComponentInChildren<PebbleManager>();
                return m_pebbleManager;
            }
        }
        #endregion

        #region Initialization
        void OnEnable()
        {
            EventManager.Instance.RegisterEventReceiver(Constants.Event.ON_CHANGE_AVATAR_MOOD, OnChangeMood);
            EventManager.Instance.RegisterEventReceiver(Constants.Event.ON_CLASSIFY_RESULT, OnClassifyResult );
            EventManager.Instance.RegisterEventReceiver(Constants.Event.ON_QUESTION_CANCEL, OnCancelQuestion );

            DebugConsole.Instance.RegisterDebugInfo("STATE", OnStateDebugInfo);
            DebugConsole.Instance.RegisterDebugInfo("MOOD", OnMoodDebugInfo);
            DebugConsole.Instance.RegisterDebugInfo("CLASS", OnClassifyDebugInfo);
            DebugConsole.Instance.RegisterDebugInfo("Q", OnQuestionDebugInfo);
            DebugConsole.Instance.RegisterDebugInfo("A", OnAnwserDebugInfo);
        }
        void OnDisable()
        {
            EventManager.Instance.UnregisterEventReceiver(Constants.Event.ON_CHANGE_AVATAR_MOOD, OnChangeMood);
            EventManager.Instance.UnregisterEventReceiver(Constants.Event.ON_CLASSIFY_RESULT, OnClassifyResult );
            EventManager.Instance.UnregisterEventReceiver(Constants.Event.ON_QUESTION_CANCEL, OnCancelQuestion );

            DebugConsole.Instance.UnregisterDebugInfo("STATE", OnStateDebugInfo);
            DebugConsole.Instance.UnregisterDebugInfo("MOOD", OnMoodDebugInfo);
            DebugConsole.Instance.UnregisterDebugInfo("CLASS", OnClassifyDebugInfo);
            DebugConsole.Instance.UnregisterDebugInfo("Q", OnQuestionDebugInfo);
            DebugConsole.Instance.UnregisterDebugInfo("A", OnAnwserDebugInfo);
        }

        /// <exclude />
        protected override void Awake()
        {
            base.Awake();
        }
        /// <exclude />
        protected override void Start()
        {
            base.Start();
            StartAvatar();
        }

        private string OnStateDebugInfo()
        {
            return State.ToString();
        }
        private string OnMoodDebugInfo()
        {
            return Mood.ToString();
        }
        private string OnClassifyDebugInfo()
        {
            if (m_ClassifyResult != null)
            {
                return string.Format("{0} ({1:0.00})",
                    m_ClassifyResult.top_class,
                    m_ClassifyResult.topConfidence);
            }
            return string.Empty;
        }
        private string OnQuestionDebugInfo()
        {
            if (m_QuestionResult != null && m_QuestionResult.HasQuestion())
            {
                return string.Format("{0} ({1:0.00})",
                    m_QuestionResult.questions[0].question.questionText,
                    m_QuestionResult.questions[0].topConfidence);
            }
            return string.Empty;
        }
        private string OnAnwserDebugInfo()
        {
            return m_LastAnswer;
        }

        private void OnNextMood()
        {
            Mood = (MoodType)((((int)Mood) + 1) % Enum.GetValues(typeof(MoodType)).Length);
        }

        private void OnExampleQuestion()
        {
            //TODO
            InstatiateQuestionWidget();
        }

        private void StartAvatar()
        {
            Log.Status("AvatarWidget", "Starting avatar.");

            State = AvatarState.SLEEPING_LISTENING;
            // Find our dialog ID
            if (!string.IsNullOrEmpty(m_DialogName))
                m_Dialog.GetDialogs(OnFindDialog);
        }

        private void OnFindDialog(Dialogs dialogs)
        {
            if (dialogs != null)
            {
                foreach (var dialog in dialogs.dialogs)
                {
                    if (dialog.name == m_DialogName)
                        m_DialogId = dialog.dialog_id;
                }
            }

            if (string.IsNullOrEmpty(m_DialogId))
            {
                Log.Error("AvatarWidget", "Failed to find dialog ID for {0}", m_DialogName);
                State = AvatarState.ERROR;
            }
        }

        #endregion

        #region Level Input
        private void OnLevelInput(Data data)
        {
            if(State== AvatarState.ANSWERING)
            {
                EventManager.Instance.SendEvent(Constants.Event.ON_AVATAR_SPEAKING, ((FloatData)data).Float);
            }
            else
            {
                EventManager.Instance.SendEvent(Constants.Event.ON_USER_SPEAKING, ((FloatData)data).Float);
            }
           
		}
        #endregion

        #region Speaking Input
        private void OnSpeaking(Data data )
        {
            SpeakingStateData bdata = data as SpeakingStateData;
            if ( bdata == null )
                throw new WatsonException( "Unexpected data type." );

            if ( State != AvatarState.ERROR )
            {
                if ( bdata.Boolean )
                    State = AvatarState.ANSWERING;
                else
                    State = m_PreviousListeningMode;
            }
        }
        #endregion

        #region Event Handlers
        private ClassifyResult GetClassifyResult( object [] args )
        {
            if ( args != null && args.Length > 0 )
                return args[0] as ClassifyResult;
            return null;
        }

        private static string PickRandomString( string [] strings )
        {
            return strings[ UnityEngine.Random.Range( 0, strings.Length ) ];
        }

        /// <summary>
        /// Event Handler for ON_CLASSIFY_FAILURE
        /// </summary>
        /// <param name="args"></param>
        public void OnClassifyFailure( object [] args )
        {
            if (State != AvatarState.SLEEPING_LISTENING)
            {
				State = AvatarState.DONT_UNDERSTAND;
                m_TextOutput.SendData(new TextData( PickRandomString( m_FailurePhrases ) ));
            }
               
            //State = AvatarState.LISTENING;
        }

        /// <summary>
        /// Event handler for ON_COMMAND_WAKEUP
        /// </summary>
        /// <param name="args"></param>
        public void OnWakeup( object [] args )
        {
            if (State == AvatarState.SLEEPING_LISTENING)
            {
                Mood = MoodType.IDLE;
                State = AvatarState.LISTENING;
                
                // start a conversation with the dialog..
                ClassifyResult result = GetClassifyResult( args );
                if ( result != null && !string.IsNullOrEmpty(m_DialogId))
                    m_Dialog.Converse(m_DialogId, result.text, OnDialogResponse, 0, m_DialogClientId);
                else
                    m_TextOutput.SendData(new TextData(PickRandomString( m_HelloPhrases ) ));
            }
        }

        /// <summary>
        /// Event handler for ON_COMMAND_SLEEP
        /// </summary>
        /// <param name="args"></param>
        public void OnSleep(object [] args)
        {
            if (State != AvatarState.SLEEPING_LISTENING)
            {
                Mood = MoodType.SLEEPING;
                State = AvatarState.SLEEPING_LISTENING;

                m_TextOutput.SendData(new TextData( PickRandomString( m_GoodbyePhrases ) ));
                if (m_FocusQuestion != null)
                    m_FocusQuestion.OnLeaveTheSceneAndDestroy();

                
                m_DialogConversationId = 0;
                m_DialogClientId = 0;
            }
        }

        public void OnClassifyResult( object [] args )
        {
            m_ClassifyResult = args[0] as ClassifyResult;
        }

        /// <summary>
        /// Event handler for ON_CLASSIFY_QUESTION
        /// </summary>
        /// <param name="args"></param>
        public void OnQuestion(object [] args)
        {
            ClassifyResult result = GetClassifyResult( args );
            if ( result == null )
                throw new WatsonException( "ClassifyResult expected." );

            if (State == AvatarState.LISTENING)
            {
                if ( result.top_class.Contains( "-" ) )
                    m_Pipeline = result.top_class.Substring( result.top_class.IndexOf('-') + 1 );

                State = AvatarState.THINKING;
                if (!m_XRAY.AskQuestion(m_Pipeline, result.text, OnAskQuestion))
                {
                    Log.Error("AvatarWidget", "Failed to send question to XRAY.");
                    State = AvatarState.ERROR;
                }
            }
        }

        public void OnCancelQuestion( object [] args )
        {
            if ( State == AvatarState.THINKING )
                State = AvatarState.LISTENING;
        }

        /// <summary>
        /// Event handler for ON_CLASSIFY_DIALOG
        /// </summary>
        /// <param name="args"></param>
        public void OnDialog(object [] args)
        {
            ClassifyResult result = GetClassifyResult( args );
            if ( result == null )
                throw new WatsonException( "ClassifyResult expected." );

            if ( State == AvatarState.LISTENING)
            {
                m_ClassifyResult = result;

                if (!string.IsNullOrEmpty(m_DialogId))
                {
                    if (m_Dialog.Converse(m_DialogId, result.text, OnDialogResponse,
                        m_DialogConversationId, m_DialogClientId))
                    {
						State = AvatarState.THINKING;
                    }
                }
            }
        }

        /// <summary>
        /// Event handler for ON_COMMAND_DEBUGON
        /// </summary>
        /// <param name="args"></param>
        public void OnDebugOn(object [] args)
        {
            DebugConsole.Instance.Active = true;
            State = AvatarState.LISTENING;
        }

        /// <summary>
        /// Event handler for ON_COMMAND_DEBUGOFF
        /// </summary>
        /// <param name="args"></param>
        public void OnDebugOff(object [] args)
        {
            DebugConsole.Instance.Active = false;
            State = AvatarState.LISTENING;
        }
        #endregion


        #region Question and Dialog Callbacks
        private void OnDialogResponse(ConverseResponse resp)
        {
            if (resp != null)
            {
                m_DialogClientId = resp.client_id;
                m_DialogConversationId = resp.conversation_id;

                if (resp.response != null)
                {
                    foreach (var t in resp.response)
                    {
                        if (!string.IsNullOrEmpty(t))
                            m_TextOutput.SendData(new TextData(t));
                    }
                }
            }
        }

        [SerializeField]
        private string m_AnswerFormatWEA = "Here is what I found in the {0} corpus.";

        private void OnAskQuestion( AskResponse response )
        {
            if ( State == AvatarState.THINKING )
            {
                if ( response != null && response.questions.HasQuestion() )
                {
                    m_QuestionResult = response.questions;
                    m_ParseData = response.parseData;
                    m_AnswerResult = response.answers;

                    InstatiateQuestionWidget();

                    EventManager.Instance.SendEvent( Constants.Event.ON_QUESTION_PIPELINE, m_Pipeline );
                    EventManager.Instance.SendEvent( Constants.Event.ON_QUESTION, m_QuestionResult );
                    EventManager.Instance.SendEvent( Constants.Event.ON_QUESTION_PARSE, m_ParseData );
                    EventManager.Instance.SendEvent( Constants.Event.ON_QUESTION_ANSWERS, m_AnswerResult );
                    EventManager.Instance.SendEvent( Constants.Event.ON_QUESTION_LOCATION, XRAY.Location );

                    if ( m_AnswerResult != null && m_AnswerResult.HasAnswer() )
                    {
                        foreach (var a in m_AnswerResult.answers)
                            Log.Debug("AvatarWidget", "A: {0} ({1})", a.answerText, a.confidence);

                        string answer = m_AnswerResult.answers[0].answerText;
                        if ( answer.Length > m_MaxAnswerLength )
                            answer = answer.Substring( 0, m_MaxAnswerLength );
                        answer = Utility.RemoveTags( answer );

                        m_LastAnswer = answer;
                        EventManager.Instance.SendEvent(Constants.Event.ON_DEBUG_MESSAGE, answer);

                        // HACK: until we know if the answer is WDA or WEA, just look at the pipeline name for now.
                        if ( m_Pipeline == "woodside" )
                        {
                            m_TextOutput.SendData( new TextData( string.Format( m_AnswerFormatWEA, m_Pipeline ) ) );
                            EventManager.Instance.SendEvent( Constants.Event.ON_COMMAND_ANSWERS );
                        }
                        else
                        {
                            m_TextOutput.SendData(new TextData(answer));
                        }
                    }

                    State = AvatarState.LISTENING;
                }
                else
                {
                    State = AvatarState.ERROR;
                }
            }
        }

        private void InstatiateQuestionWidget()
        {
            if (m_FocusQuestion != null) {
                m_FocusQuestion.OnLeaveTheSceneAndDestroy();
            }
            
            if (m_QuestionPrefab != null)	//m_FocusQuestion == null && 
            {
                GameObject questionObject = GameObject.Instantiate(m_QuestionPrefab);
                m_FocusQuestion = questionObject.GetComponentInChildren<QuestionWidget>();
                if (m_FocusQuestion == null)
                    throw new WatsonException("Question prefab is missing QuestionWidget");
                m_FocusQuestion.Focused = true;	//currently our focus object
            }
        }

        #endregion

        #region Avatar Mood / Behavior

        [Serializable]
        private class AvatarStateInfo
        {
            public AvatarState m_State;
            public Color m_Color;
            public float m_Speed;
        };

        [SerializeField]
        private AvatarStateInfo[] m_StateInfo = new AvatarStateInfo[]
        {
            new AvatarStateInfo() { m_State = AvatarState.CONNECTING, m_Color = new Color(241 / 255.0f, 241 / 255.0f, 242 / 255.0f), m_Speed = 0.0f },
            new AvatarStateInfo() { m_State = AvatarState.SLEEPING_LISTENING, m_Color = new Color(241 / 255.0f, 241 / 255.0f, 242 / 255.0f), m_Speed = 0.0f },
            new AvatarStateInfo() { m_State = AvatarState.LISTENING, m_Color = new Color(0 / 255.0f, 166 / 255.0f, 160 / 255.0f), m_Speed = 1.0f },
            new AvatarStateInfo() { m_State = AvatarState.THINKING, m_Color = new Color(238 / 255.0f, 62 / 255.0f, 150 / 255.0f), m_Speed = 1.0f },
            new AvatarStateInfo() { m_State = AvatarState.ANSWERING, m_Color = new Color(140 / 255.0f, 198 / 255.0f, 63 / 255.0f), m_Speed = 1.0f },
            new AvatarStateInfo() { m_State = AvatarState.ERROR, m_Color = new Color(255 / 255.0f, 0 / 255.0f, 0 / 255.0f), m_Speed = 0.0f },
        };

        public Color BehaviourColor
        {
            get
            {
                foreach (var c in m_StateInfo)
                    if (c.m_State == m_State)
                        return c.m_Color;

                Log.Warning("AvatarWidget", "StateColor not defined for state {0}.", m_State.ToString());
                return Color.white;
            }
        }

        private MoodType m_currentMood = MoodType.NONE;
        public MoodType Mood
        {
            get
            {
                return m_currentMood;
            }
            set
            {
                if (m_currentMood != value)
                {
                    m_currentMood = value;
                    EventManager.Instance.SendEvent(Constants.Event.ON_CHANGE_AVATAR_MOOD_FINISH, (int)value);
                }
            }
        }

        public MoodType[] MoodTypeList
        {
            get
            {
                return Enum.GetValues(typeof(MoodType)) as MoodType[];
            }
        }

        public float BehaviorSpeedModifier
        {
            get
            {
                foreach (var info in m_StateInfo)
                    if (info.m_State == State)
                        return info.m_Speed;

                Log.Warning("AvatarWidget", "StateInfo not defined for {0}.", State.ToString());
                return 1.0f;
            }
        }

        public float BehaviorTimeModifier
        {
            get
            {
                float value = BehaviorSpeedModifier;
                if (value != 0.0f)
                    value = 1.0f / value;

                return value;
            }
        }

        [Serializable]
        private class AvatarMoodInfo
        {
            public MoodType m_Mood;
            public Color m_Color;
            public float m_Speed;
        };

        [SerializeField]
        private AvatarMoodInfo[] m_MoodInfo = new AvatarMoodInfo[]
        {
            new AvatarMoodInfo() { m_Mood = MoodType.SLEEPING, m_Color = new Color(255 / 255.0f, 255 / 255.0f, 255 / 255.0f), m_Speed = 0.0f },
            new AvatarMoodInfo() { m_Mood = MoodType.IDLE, m_Color = new Color(241 / 255.0f, 241 / 255.0f, 242 / 255.0f), m_Speed = 1.0f },
            new AvatarMoodInfo() { m_Mood = MoodType.INTERESTED, m_Color = new Color(131 / 255.0f, 209 / 255.0f, 245 / 255.0f), m_Speed = 1.1f },
            new AvatarMoodInfo() { m_Mood = MoodType.URGENT, m_Color = new Color(221 / 255.0f, 115 / 255.0f, 28 / 255.0f), m_Speed = 2.0f },
            new AvatarMoodInfo() { m_Mood = MoodType.UPSET, m_Color = new Color(217 / 255.0f, 24 / 255.0f, 45 / 255.0f), m_Speed = 1.5f },
            new AvatarMoodInfo() { m_Mood = MoodType.SHY, m_Color = new Color(243 / 255.0f, 137 / 255.0f, 175 / 255.0f), m_Speed = 0.9f },
        };

        public Color MoodColor
        {
            get
            {
                foreach (var c in m_MoodInfo)
                    if (c.m_Mood == Mood)
                        return c.m_Color;

                Log.Warning("AvatarWidget", "Mood not defined for {0}.", Mood.ToString());
                return Color.white;
            }
        }

        public float MoodSpeedModifier
        {
            get
            {
                foreach (var c in m_MoodInfo)
                    if (c.m_Mood == Mood)
                        return c.m_Speed;

                Log.Warning("AvatarWidget", "Mood not defined for {0}.", Mood.ToString());
                return 1.0f;
            }
        }

        public float MoodTimeModifier
        {
            get
            {
                float value = MoodSpeedModifier;
                if (value != 0.0f)
                    value = 1.0f / value;

                return value;
            }
        }


        void OnChangeMood(System.Object[] args)
        {
            if (args.Length == 1)
            {
                Mood = (MoodType)args[0];
            }
        }
        #endregion
    }
}
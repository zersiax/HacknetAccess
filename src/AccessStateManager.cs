using System;

namespace HacknetAccess
{
    /// <summary>
    /// Central state manager for accessibility patches.
    /// Tracks which UI context is active so patches know when to announce.
    /// Only one state is active at a time.
    /// </summary>
    public static class AccessStateManager
    {
        /// <summary>
        /// Available accessibility states/modes.
        /// </summary>
        public enum State
        {
            None,
            MainMenu,
            LoginScreen,
            MessageBox,
            Gameplay,
            OptionsMenu
        }

        /// <summary>
        /// Context where the mod is operating.
        /// </summary>
        public enum Context
        {
            Unknown,
            TitleScreen,
            Gameplay
        }

        /// <summary>
        /// Currently active state.
        /// </summary>
        public static State Current { get; private set; } = State.None;

        /// <summary>
        /// Current context.
        /// </summary>
        public static Context CurrentContext { get; private set; } = Context.Unknown;

        /// <summary>
        /// Event fired when state changes. Parameters: (oldState, newState)
        /// </summary>
        public static event Action<State, State> OnStateChanged;

        /// <summary>
        /// Event fired when context changes. Parameters: (oldContext, newContext)
        /// </summary>
        public static event Action<Context, Context> OnContextChanged;

        /// <summary>
        /// Set the current context. Resets state when context changes.
        /// </summary>
        public static void SetContext(Context context)
        {
            if (CurrentContext == context) return;

            var oldContext = CurrentContext;

            if (Current != State.None)
            {
                DebugLogger.Log(LogCategory.State, "AccessState",
                    $"Context change {oldContext} -> {context}, resetting {Current}");
                ForceReset();
            }

            CurrentContext = context;
            DebugLogger.Log(LogCategory.State, "AccessState", $"Context: {context}");
            OnContextChanged?.Invoke(oldContext, context);
        }

        /// <summary>
        /// Try to enter a new state. Automatically exits the previous state.
        /// </summary>
        /// <returns>true if state was entered successfully</returns>
        public static bool TryEnter(State state)
        {
            if (state == State.None) return false;
            if (Current == state) return true;

            if (Current != State.None)
            {
                DebugLogger.Log(LogCategory.State, "AccessState",
                    $"Auto-exiting {Current} for {state}");
                var previousState = Current;
                Current = State.None;
                OnStateChanged?.Invoke(previousState, State.None);
            }

            var oldState = Current;
            Current = state;
            DebugLogger.Log(LogCategory.State, "AccessState", $"Entered {state}");
            OnStateChanged?.Invoke(oldState, state);
            return true;
        }

        /// <summary>
        /// Exit from a state. Only exits if currently in that state.
        /// </summary>
        public static void Exit(State state)
        {
            if (Current != state) return;

            var oldState = Current;
            Current = State.None;
            DebugLogger.Log(LogCategory.State, "AccessState", $"Exited {state}");
            OnStateChanged?.Invoke(oldState, State.None);
        }

        /// <summary>
        /// Force exit from any state.
        /// </summary>
        public static void ForceReset()
        {
            if (Current != State.None)
            {
                var oldState = Current;
                Current = State.None;
                DebugLogger.Log(LogCategory.State, "AccessState", $"Force reset from {oldState}");
                OnStateChanged?.Invoke(oldState, State.None);
            }
        }

        /// <summary>
        /// Check if currently in a specific state.
        /// </summary>
        public static bool IsIn(State state)
        {
            return Current == state;
        }

        /// <summary>
        /// Global frame counter, incremented each Game1.Update.
        /// Used by patches to detect when a daemon stops drawing.
        /// </summary>
        public static int FrameCount { get; private set; }

        /// <summary>
        /// Called once per frame from Game1.Update to advance the counter.
        /// </summary>
        public static void Tick()
        {
            FrameCount++;
        }
    }
}

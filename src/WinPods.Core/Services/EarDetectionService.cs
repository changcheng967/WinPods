using System.Diagnostics;
using WinPods.Core.Models;

namespace WinPods.Core.Services
{
    /// <summary>
    /// Service for detecting AirPods ear insertion/removal and triggering media control.
    /// Uses 500ms debounce to prevent rapid state changes.
    /// </summary>
    public class EarDetectionService
    {
        private EarDetectionState _currentState = EarDetectionState.BothInCase;
        private EarDetectionState? _pendingState = null;
        private DateTime _lastStateChange = DateTime.MinValue;
        private readonly Timer _debounceTimer;
        private readonly object _stateLock = new object();

        /// <summary>
        /// Raised when earbuds are removed (transition from in-ear to not in-ear).
        /// </summary>
        public event EventHandler<EarDetectionEventArgs>? EarbudsRemoved;

        /// <summary>
        /// Raised when earbuds are inserted (transition from not in-ear to in-ear).
        /// </summary>
        public event EventHandler<EarDetectionEventArgs>? EarbudsInserted;

        /// <summary>
        /// Raised when ear detection state changes (for UI/debugging).
        /// </summary>
        public event EventHandler<EarDetectionEventArgs>? StateChanged;

        /// <summary>
        /// Gets the current ear detection state.
        /// </summary>
        public EarDetectionState CurrentState
        {
            get
            {
                lock (_stateLock)
                {
                    return _currentState;
                }
            }
        }

        /// <summary>
        /// Initializes a new instance of the EarDetectionService.
        /// </summary>
        public EarDetectionService()
        {
            // Timer for debounce - checks every 100ms if we should commit the pending state
            _debounceTimer = new Timer(OnDebounceTimer, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Processes a new AirPods state update and checks for ear detection changes.
        /// </summary>
        /// <param name="state">Current AirPods state</param>
        public void ProcessStateUpdate(AirPodsState state)
        {
            if (state == null || !state.IsConnected)
            {
                return;
            }

            // Parse ear status from the PodStatus enum
            var newState = ParseFromPodStatus(state.PodStatus);

            lock (_stateLock)
            {
                // Skip if state hasn't changed
                if (newState == _currentState && _pendingState == null)
                {
                    return;
                }

                // Skip if the new state is the same as pending state
                if (_pendingState.HasValue && newState == _pendingState.Value)
                {
                    return;
                }

                // Store the new state as pending
                _pendingState = newState;
                _lastStateChange = DateTime.UtcNow;

                // Start debounce timer (check every 100ms, commit after 500ms)
                _debounceTimer.Change(100, 100);
            }

            Debug.WriteLine($"[EarDetection] State change detected: {EarStatusParser.GetDescription(_currentState)} -> {EarStatusParser.GetDescription(newState)} (debouncing...)");
        }

        /// <summary>
        /// Parses ear detection state from PodStatus enum.
        /// </summary>
        private static EarDetectionState ParseFromPodStatus(PodStatus podStatus)
        {
            bool leftInEar = (podStatus & PodStatus.LeftInEar) == PodStatus.LeftInEar;
            bool rightInEar = (podStatus & PodStatus.RightInEar) == PodStatus.RightInEar;
            bool leftInCase = (podStatus & PodStatus.LeftInCase) == PodStatus.LeftInCase;
            bool rightInCase = (podStatus & PodStatus.RightInCase) == PodStatus.RightInCase;

            if (leftInEar && rightInEar)
            {
                return EarDetectionState.BothInEar;
            }
            else if (leftInEar && !rightInEar)
            {
                return EarDetectionState.LeftInEar;
            }
            else if (!leftInEar && rightInEar)
            {
                return EarDetectionState.RightInEar;
            }
            else if (leftInCase && rightInCase)
            {
                return EarDetectionState.BothInCase;
            }
            else
            {
                // One or both are out (not in ear, not in case)
                return EarDetectionState.OneOrBothOut;
            }
        }

        /// <summary>
        /// Debounce timer callback - commits state change after 500ms of stability.
        /// </summary>
        private void OnDebounceTimer(object? state)
        {
            lock (_stateLock)
            {
                if (!_pendingState.HasValue)
                {
                    // No pending state, stop timer
                    _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    return;
                }

                // Check if 500ms has elapsed since the last state change
                var elapsed = DateTime.UtcNow - _lastStateChange;
                if (elapsed.TotalMilliseconds >= 500)
                {
                    // Commit the state change
                    var oldState = _currentState;
                    var newState = _pendingState.Value;
                    _currentState = newState;
                    _pendingState = null;

                    // Stop timer
                    _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);

                    // Raise events based on state transition
                    var wasInEar = EarStatusParser.IsAnyInEar(oldState);
                    var isInEar = EarStatusParser.IsAnyInEar(newState);

                    var args = new EarDetectionEventArgs(oldState, newState);

                    if (wasInEar && !isInEar)
                    {
                        // Earbuds removed
                        Debug.WriteLine($"[EarDetection] EARBUDS REMOVED: {EarStatusParser.GetDescription(oldState)} -> {EarStatusParser.GetDescription(newState)}");
                        EarbudsRemoved?.Invoke(this, args);
                    }
                    else if (!wasInEar && isInEar)
                    {
                        // Earbuds inserted
                        Debug.WriteLine($"[EarDetection] EARBUDS INSERTED: {EarStatusParser.GetDescription(oldState)} -> {EarStatusParser.GetDescription(newState)}");
                        EarbudsInserted?.Invoke(this, args);
                    }

                    // Always raise state changed event
                    StateChanged?.Invoke(this, args);
                }
            }
        }

        /// <summary>
        /// Resets the service to initial state.
        /// </summary>
        public void Reset()
        {
            lock (_stateLock)
            {
                _currentState = EarDetectionState.BothInCase;
                _pendingState = null;
                _lastStateChange = DateTime.MinValue;
                _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }
    }

    /// <summary>
    /// Event arguments for ear detection events.
    /// </summary>
    public class EarDetectionEventArgs : EventArgs
    {
        /// <summary>
        /// Previous ear detection state.
        /// </summary>
        public EarDetectionState OldState { get; }

        /// <summary>
        /// New ear detection state.
        /// </summary>
        public EarDetectionState NewState { get; }

        /// <summary>
        /// Timestamp of the state change.
        /// </summary>
        public DateTime Timestamp { get; }

        public EarDetectionEventArgs(EarDetectionState oldState, EarDetectionState newState)
        {
            OldState = oldState;
            NewState = newState;
            Timestamp = DateTime.UtcNow;
        }
    }
}

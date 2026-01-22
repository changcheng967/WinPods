using System.Diagnostics;
using Windows.Media.Control;
using Windows.Storage.Streams;
using System.Runtime.InteropServices;
using WinPods.Core.Models;

namespace WinPods.App.Services
{
    /// <summary>
    /// Controls media playback using Windows GlobalSystemMediaTransportControlsSessionManager.
    /// Falls back to keyboard simulation (VK_MEDIA_PLAY_PAUSE) if needed.
    /// </summary>
    public class MediaController
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private bool _wePausedIt = false;

        // Keyboard simulation fallback
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, int dwExtraInfo);

        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        /// <summary>
        /// Initializes the media controller.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                Debug.WriteLine("[MediaController] Initialized with GlobalSystemMediaTransportControlsSessionManager");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaController] Failed to initialize: {ex.Message}. Will use keyboard fallback.");
            }
        }

        /// <summary>
        /// Pauses the currently playing media.
        /// Returns true if media was successfully paused.
        /// </summary>
        public async Task<bool> PauseAsync()
        {
            Console.WriteLine("[MediaController] ========== PAUSE REQUEST ==========");

            try
            {
                if (_sessionManager != null)
                {
                    Console.WriteLine("[MediaController] ✓ SessionManager is NOT null");

                    // Get the current session
                    var currentSession = _sessionManager.GetCurrentSession();
                    Console.WriteLine($"[MediaController] CurrentSession: {(currentSession != null ? "FOUND" : "NULL")}");

                    if (currentSession != null)
                    {
                        var playbackInfo = currentSession.GetPlaybackInfo();
                        Console.WriteLine($"[MediaController] PlaybackInfo: {(playbackInfo != null ? "FOUND" : "NULL")}");

                        if (playbackInfo != null)
                        {
                            var playbackStatus = playbackInfo.PlaybackStatus;
                            Console.WriteLine($"[MediaController] Current playback status: {playbackStatus}");

                            // Only pause if currently playing
                            if (playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                            {
                                Console.WriteLine("[MediaController] Status is PLAYING - attempting TryPauseAsync()...");
                                var result = await currentSession.TryPauseAsync();
                                _wePausedIt = true;

                                if (result)
                                {
                                    Console.WriteLine("[MediaController] ✓✓✓ SUCCESS - Paused media using GlobalSystemMediaTransportControlsSessionManager");
                                }
                                else
                                {
                                    Console.WriteLine("[MediaController] ❌ TryPauseAsync() returned false - trying keyboard fallback");
                                }

                                if (result)
                                    return true;
                                else
                                {
                                    // Continue to keyboard fallback
                                    Console.WriteLine("[MediaController] Falling back to keyboard simulation");
                                    return PauseWithKeyboard();
                                }
                            }
                            else
                            {
                                Console.WriteLine($"[MediaController] Not pausing - status is {playbackStatus}, not Playing");
                                return false;
                            }
                        }
                        else
                        {
                            Console.WriteLine("[MediaController] ❌ PlaybackInfo is NULL");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[MediaController] ❌ SessionManager is NULL");
                }

                // Fallback: Try keyboard simulation
                Console.WriteLine("[MediaController] Falling back to keyboard simulation");
                return PauseWithKeyboard();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaController] ❌ Pause failed with exception: {ex.Message}");
                Console.WriteLine($"[MediaController] Exception type: {ex.GetType().Name}");
                // Try keyboard fallback
                return PauseWithKeyboard();
            }
        }

        /// <summary>
        /// Resumes media playback (only if we paused it).
        /// Returns true if media was successfully resumed.
        /// </summary>
        public async Task<bool> ResumeAsync()
        {
            Console.WriteLine("[MediaController] ========== RESUME REQUEST ==========");
            Console.WriteLine($"[MediaController] _wePausedIt flag: {_wePausedIt}");

            // Only resume if we're the one who paused it
            if (!_wePausedIt)
            {
                Console.WriteLine("[MediaController] ❌ Not resuming - we didn't pause it");
                return false;
            }

            try
            {
                if (_sessionManager != null)
                {
                    Console.WriteLine("[MediaController] ✓ SessionManager is NOT null");

                    var currentSession = _sessionManager.GetCurrentSession();
                    Console.WriteLine($"[MediaController] CurrentSession: {(currentSession != null ? "FOUND" : "NULL")}");

                    if (currentSession != null)
                    {
                        var playbackInfo = currentSession.GetPlaybackInfo();
                        Console.WriteLine($"[MediaController] PlaybackInfo: {(playbackInfo != null ? "FOUND" : "NULL")}");

                        if (playbackInfo != null)
                        {
                            var playbackStatus = playbackInfo.PlaybackStatus;
                            Console.WriteLine($"[MediaController] Current playback status: {playbackStatus}");

                            // Only resume if currently paused
                            if (playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                            {
                                Console.WriteLine("[MediaController] Status is PAUSED - attempting TryPlayAsync()...");
                                var result = await currentSession.TryPlayAsync();
                                _wePausedIt = false;

                                if (result)
                                {
                                    Console.WriteLine("[MediaController] ✓✓✓ SUCCESS - Resumed media using GlobalSystemMediaTransportControlsSessionManager");
                                }
                                else
                                {
                                    Console.WriteLine("[MediaController] ❌ TryPlayAsync() returned false");
                                }

                                return result;
                            }
                            else
                            {
                                Console.WriteLine($"[MediaController] ❌ Not resuming - status is {playbackStatus}, not Paused");
                                Console.WriteLine("[MediaController] Resetting _wePausedIt flag since media is already playing");
                                _wePausedIt = false;
                                return false;
                            }
                        }
                        else
                        {
                            Console.WriteLine("[MediaController] ❌ PlaybackInfo is NULL");
                        }
                    }
                }
                else
                {
                    Console.WriteLine("[MediaController] ❌ SessionManager is NULL");
                }

                // Fallback: Try keyboard simulation
                Console.WriteLine("[MediaController] Falling back to keyboard simulation");
                return ResumeWithKeyboard();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[MediaController] ❌ Resume failed with exception: {ex.Message}");
                Console.WriteLine($"[MediaController] Exception type: {ex.GetType().Name}");
                Console.WriteLine($"[MediaController] Stack trace: {ex.StackTrace}");
                // Try keyboard fallback
                return ResumeWithKeyboard();
            }
        }

        /// <summary>
        /// Resets the "we paused it" flag (e.g., when user manually resumes).
        /// </summary>
        public void ResetPauseFlag()
        {
            _wePausedIt = false;
            Debug.WriteLine("[MediaController] Reset pause flag");
        }

        /// <summary>
        /// Pauses using keyboard simulation (VK_MEDIA_PLAY_PAUSE).
        /// </summary>
        private bool PauseWithKeyboard()
        {
            try
            {
                // Check current playback status first
                var currentSession = _sessionManager?.GetCurrentSession();
                if (currentSession != null)
                {
                    var playbackInfo = currentSession.GetPlaybackInfo();
                    if (playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                    {
                        Debug.WriteLine("[MediaController] Already paused, skipping keyboard simulation");
                        return false;
                    }
                }

                // Send play/pause key press
                keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, 0);
                keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, 0);
                _wePausedIt = true;
                Debug.WriteLine("[MediaController] Sent VK_MEDIA_PLAY_PAUSE (pause)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaController] Keyboard pause failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Resumes using keyboard simulation (VK_MEDIA_PLAY_PAUSE).
        /// </summary>
        private bool ResumeWithKeyboard()
        {
            if (!_wePausedIt)
            {
                return false;
            }

            try
            {
                // Check current playback status first
                var currentSession = _sessionManager?.GetCurrentSession();
                if (currentSession != null)
                {
                    var playbackInfo = currentSession.GetPlaybackInfo();
                    if (playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        Debug.WriteLine("[MediaController] Already playing, skipping keyboard simulation");
                        _wePausedIt = false;
                        return false;
                    }
                }

                // Send play/pause key press
                keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, 0);
                keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, 0);
                _wePausedIt = false;
                Debug.WriteLine("[MediaController] Sent VK_MEDIA_PLAY_PAUSE (resume)");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaController] Keyboard resume failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets the current media playback status.
        /// </summary>
        public async Task<GlobalSystemMediaTransportControlsSessionPlaybackStatus?> GetPlaybackStatusAsync()
        {
            try
            {
                if (_sessionManager != null)
                {
                    var currentSession = _sessionManager.GetCurrentSession();
                    if (currentSession != null)
                    {
                        var playbackInfo = currentSession.GetPlaybackInfo();
                        return playbackInfo?.PlaybackStatus;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaController] Failed to get playback status: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Gets info about the currently playing media.
        /// </summary>
        public async Task<string?> GetCurrentMediaInfoAsync()
        {
            try
            {
                if (_sessionManager != null)
                {
                    var currentSession = _sessionManager.GetCurrentSession();
                    if (currentSession != null)
                    {
                        var mediaProperties = await currentSession.TryGetMediaPropertiesAsync();
                        if (mediaProperties != null)
                        {
                            return $"{mediaProperties.Artist} - {mediaProperties.Title}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaController] Failed to get media info: {ex.Message}");
            }

            return null;
        }
    }
}

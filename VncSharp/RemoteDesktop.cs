// VncSharp - .NET VNC Client Library
// Copyright (C) 2008 David Humphrey
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA

#if DEBUG
#endif
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using VncSharp;

// ReSharper disable ArrangeAccessorOwnerBody

// ReSharper disable PossibleLossOfFraction
#pragma warning disable 1587, 1584, 1711, 1572, 1581, 1580

namespace VncSharp4Unity2D
{
    /// <summary>
    /// Event Handler delegate declaration used by events that signal successful connection with the server.
    /// </summary>
    public delegate void ConnectCompleteHandler(object sender, ConnectEventArgs e);

    /// <summary>
    /// When connecting to a VNC Host, a password will sometimes be required.  Therefore a password must be
    /// obtained from the user.  A default Password dialog box is included and will be used unless users of the
    /// control provide their own Authenticate delegate function for the task.  For example, this might pull a
    /// password from a configuration file of some type instead of prompting the user.
    /// </summary>
    public delegate string AuthenticateDelegate();

    /// <summary>
    /// SpecialKeys is a list of the various keyboard combinations that overlap with the client-side and make it
    /// difficult to send remotely.  These values are used in conjunction with the SendSpecialKeys method.
    /// </summary>
    public enum SpecialKeys
    {
        CtrlAltDel,
        AltF4,
        CtrlEsc,
        Ctrl,
        Alt
    }

    [ToolboxBitmap(typeof(RemoteDesktop), "Resources.vncviewer.ico")]
    /// <summary>
    /// The RemoteDesktop control takes care of all the necessary RFB Protocol and GUI handling, including mouse
    /// and keyboard support, as well as requesting and processing screen updates from the remote VNC host.
    /// Most users will choose to use the RemoteDesktop control alone and not use any of the other protocol
    /// classes directly.
    /// </summary>
    public class RemoteDesktop
    {
        [Description("Raised after a successful call to the Connect() method.")]
        /// <summary>
        /// Raised after a successful call to the Connect() method.  Includes information for updating the local
        /// display in ConnectEventArgs.
        /// </summary>
        public event ConnectCompleteHandler ConnectComplete;

        [Description("Raised when the VNC Host drops the connection.")]
        /// <summary>
        /// Raised when the VNC Host drops the connection.
        /// </summary>
        public event EventHandler ConnectionLost;

        [Description("Raised when the VNC Host sends text to the client's clipboard.")]
        /// <summary>
        /// Raised when the VNC Host sends text to the client's clipboard. 
        /// </summary>
        public event EventHandler ClipboardChanged;

        /// <summary>
        /// Points to a Function capable of obtaining a user's password.  By default this means using the
        /// PasswordDialog.GetPassword() function; however, users of RemoteDesktop can replace this with any
        /// function they like, so long as it matches the delegate type.
        /// </summary>
        // ReSharper disable once MemberCanBePrivate.Global
        // ReSharper disable once FieldCanBeMadeReadOnly.Global
        public AuthenticateDelegate GetPassword;
        
        private Bitmap desktop; // Internal representation of remote image.
        private VncClient vnc; // The Client object handling all protocol-level interaction

        private string host;
        private int port; // The port to connect to on remote host (5900 is default)
        private int display;
        private string password;
        
        private bool passwordPending; // After Connect() is called, a password might be required.
        private RuntimeState state = RuntimeState.Disconnected;
        private bool viewOnlyMode = false;
        private int bitsPerPixel = 0;
        private int depth = 0;


        

        //private KeyboardHook _keyboardHook = new KeyboardHook();

        private enum RuntimeState
        {
            Disconnected,
            Disconnecting,
            Connected,
            Connecting
        }

        public VncClient VncClient
        {
            get { return vnc;}
        }

        public bool Connected
        {
            get { return this.VncClient.RfbProtocol.TcpClient.Connected; }
        }


        public RemoteDesktop()
        {

            // Delegate that retrieves the given password
            GetPassword = () => password;

        }

        public RemoteDesktop(string host, int port, string password) : this(host, port, 0, password)
        {
        }


        public RemoteDesktop(string host, int port, int display, string password)
        {
            this.host = host;
            this.port = port;
            this.display = display;
            this.password = password;
            // Delegate that retrieves the given password
            GetPassword = () => password;

        }

        [DefaultValue(5900)]
        [Description("The port number used by the VNC Host (typically 5900)")]
        /// <summary>
        /// The port number used by the VNC Host (typically 5900).
        /// </summary>
        // ReSharper disable once MemberCanBePrivate.Global
        public int VncPort
        {
            get { return port; }
            set
            {
                // Ignore attempts to use invalid port numbers
                if (value < 1 || value > 65535) value = 5900;
                port = value;
            }
        }

        /// <summary>
        /// True if the RemoteDesktop is connected and authenticated (if necessary) with a remote VNC Host;
        /// otherwise False.
        /// </summary>
        // ReSharper disable once MemberCanBePrivate.Global
        public bool IsConnected
        {
            get { return state == RuntimeState.Connected; }
        }

        // This is a hack to get around the issue of DesignMode returning
        // false when the control is being removed from a form at design time.
        // First check to see if the control is in DesignMode, then work up 
        // to also check any parent controls.  DesignMode returns False sometimes
        // when it is really True for the parent. Thanks to Claes Bergefall for the idea.
        private bool DesignMode
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Returns a more appropriate default size for initial drawing of the control at design time
        /// </summary>
        private Size DefaultSize
        {
            get { return new Size(400, 200); }
        }

        [Description("The name of the remote desktop.")]
        /// <summary>
        /// The name of the remote desktop, or "Disconnected" if not connected.
        /// </summary>
        public string Hostname
        {
            get { return vnc == null ? "Disconnected" : vnc.HostName; }
        }

        /// <summary>
        /// The image of the remote desktop.
        /// </summary>
        public Image Desktop
        {
            get { return desktop; }
            set
            {
                desktop = (Bitmap)value;
            }
        }

        /// <summary>
        /// Get a complete update of the entire screen from the remote host.
        /// </summary>
        /// <remarks>You should allow users to call FullScreenUpdate in order to correct
        /// corruption of the local image.  This will simply request that the next update be
        /// for the full screen, and not a portion of it.  It will not do the update while
        /// blocking.
        /// </remarks>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the
        /// Connected state.  See <see cref="IsConnected" />.</exception>
        public void FullScreenUpdate()
        {
            InsureConnection(true);
            vnc.FullScreenRefresh = true;
        }

        /// <summary>
        /// Insures the state of the connection to the server, either Connected or Not Connected depending
        /// on the value of the connected argument.
        /// </summary>
        /// <param name="connected">True if the connection must be established already, otherwise False.</param>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is in the wrong
        /// state.</exception>
        private void InsureConnection(bool connected)
        {
#if DEBUG
            // Grab the name of the calling routine:
            var methodName = new StackTrace().GetFrame(1).GetMethod().Name;
#endif
            if (connected)
            {
#if DEBUG
               
                Assert(state == RuntimeState.Connected || 
                        state == RuntimeState.Disconnecting, // special case for Disconnect()
                        $"RemoteDesktop must be in RuntimeState.Connected before calling {methodName}.");
#endif
                if (state != RuntimeState.Connected && state != RuntimeState.Disconnecting)
                    throw new InvalidOperationException(
                        "RemoteDesktop must be in Connected state before calling methods that " +
                        "require an established connection.");
            }
            else
            {
                // disconnected
#if DEBUG
                Assert(state == RuntimeState.Disconnected,
				    $"RemoteDesktop must be in RuntimeState.Disconnected before calling {methodName}.");
#endif
                if (state != RuntimeState.Disconnected && state != RuntimeState.Disconnecting)
                    throw new InvalidOperationException(
                        "RemoteDesktop cannot be in Connected state when calling methods that establish a connection.");
            }
        }

        // This event handler deals with Framebuffer Updates coming from the host. An
        // EncodedRectangle object is passed via the VncEventArgs (actually an IDesktopUpdater
        // object so that *only* Draw() can be called here--Decode() is done elsewhere).
        // The VncClient object handles thread marshalling onto the UI thread.
        private void VncUpdate(object sender, VncEventArgs e)
        {
            e.DesktopUpdater.Draw(desktop);

            if (state != RuntimeState.Connected) return;

            // Make sure the next screen update is incremental
            vnc.FullScreenRefresh = false;
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.
        /// See <see cref="IsConnected" />.</exception>
        public void Connect(string host)
        {
            // Use Display defined, (display 1 by default).
            Connect(host, display);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.
        /// See <see cref="IsConnected" />.</exception>
        public void Connect(string host, bool viewOnly)
        {
            // Use Display defined, (display 1 by default).
            Connect(host, display, viewOnly);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <param name="scaled">Determines whether to use desktop scaling or leave it normal and clip.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.
        /// See <see cref="IsConnected" />.</exception>
        public void Connect(string host, bool viewOnly, bool scaled)
        {
            // Use Display defined, (display 1 by default).
            Connect(host, display, viewOnly, scaled);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.
        /// See <see cref="IsConnected" />.</exception>
        public void Connect(string host, int display)
        {
            Connect(host, display, viewOnlyMode);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.
        /// See <see cref="IsConnected" />.</exception>
        public void Connect(string host, int display, bool viewOnly)
        {
            Connect(host, display, viewOnly, false);
        }

        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <param name="host">The IP Address or Host Name of the VNC Host.</param>
        /// <param name="display">The Display number (used on Unix hosts).</param>
        /// <param name="viewOnly">Determines whether mouse and keyboard events will be sent to the host.</param>
        /// <param name="scaled">Determines whether to use desktop scaling or leave it normal and clip.</param>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.
        /// See <see cref="IsConnected" />.</exception>
        public void Connect(string host, int display, bool viewOnly, bool scaled)
        {
            // TODO: Should this be done asynchronously so as not to block the UI?  Since an event 
            // indicates the end of the connection, maybe that would be a better design.
            InsureConnection(false);

            if (host == null) throw new ArgumentNullException(nameof(host));
            if (display < 0)
                throw new ArgumentOutOfRangeException(nameof(display), display,
                    "Display number must be a positive integer.");

            // Start protocol-level handling and determine whether a password is needed
            vnc = new VncClient();
            vnc.ConnectionLost += VncClientConnectionLost;
            vnc.ServerCutText += VncServerCutText;
            vnc.ViewOnly = viewOnly;

            passwordPending = vnc.Connect(host, display, VncPort, viewOnly);

//            SetScalingMode(scaled);

            if (passwordPending)
            {
                // Server needs a password, so call which ever method is refered to by the GetPassword delegate.
                var password = GetPassword();

                if (password != null)
                    Authenticate(password);
            }
            else
            {
                // No password needed, so go ahead and Initialize here
                Initialize();
            }
        }
        
        
        /// <summary>
        /// Connect to a VNC Host and determine whether or not the server requires a password.
        /// </summary>
        /// <exception cref="System.ArgumentNullException">Thrown if host is null.</exception>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown if display is negative.</exception>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.
        /// See <see cref="IsConnected" />.</exception>
        public void Connect()
        {
            // TODO: Should this be done asynchronously so as not to block the UI?  Since an event 
            // indicates the end of the connection, maybe that would be a better design.
            InsureConnection(false);

            if (host == null) throw new ArgumentNullException(nameof(host));
            if (display < 0)
                throw new ArgumentOutOfRangeException(nameof(display), display,
                    "Display number must be a positive integer.");

            // Start protocol-level handling and determine whether a password is needed
            vnc = new VncClient();
            vnc.ConnectionLost += VncClientConnectionLost;
            vnc.ServerCutText += VncServerCutText;
            vnc.ViewOnly = viewOnlyMode;

            passwordPending = vnc.Connect(host, display, VncPort, viewOnlyMode);

//            SetScalingMode(scaled);

            if (passwordPending)
            {
                
                if (password != null)
                    Authenticate(password);
            }
            else
            {
                // No password needed, so go ahead and Initialize here
                Initialize();
            }
        }

        /// <summary>
        /// Authenticate with the VNC Host using a user supplied password.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already Connected.
        /// See <see cref="IsConnected" />.</exception>
        /// <exception cref="System.NullReferenceException">Thrown if the password is null.</exception>
        /// <param name="password">The user's password.</param>
        private void Authenticate(string password)
        {
            InsureConnection(false);
            if (!passwordPending)
                throw new InvalidOperationException(
                    "Authentication is only required when Connect() returns True and the VNC Host " +
                    "requires a password.");
            if (password == null) throw new NullReferenceException("password");

            passwordPending = false; // repeated calls to Authenticate should fail.
            if (vnc.Authenticate(password))
                Initialize();
            else
                OnConnectionLost();
        }

        //[DefaultValue(false)]
        //[Description("True if view-only mode is desired (no mouse/keyboard events will be sent)")]
        ///// <summary>
        ///// True if view-only mode is desired (no mouse/keyboard events will be sent).
        ///// </summary>
        public bool ViewOnly
        {
            get { return viewOnlyMode; }
            set { viewOnlyMode = value; }
        }

//        /// <summary>
//        /// Set the remote desktop's scaling mode.
//        /// </summary>
//        /// <param name="scaled">Determines whether to use desktop scaling or leave it normal and clip.</param>
//        private void SetScalingMode(bool scaled)
//        {
//            if (scaled)
//                desktopPolicy = new VncScaledDesktopPolicy(vnc, this);
//            else
//                desktopPolicy = new VncClippedDesktopPolicy(vnc, this);
//
//            AutoScroll = desktopPolicy.AutoScroll;
//            AutoScrollMinSize = desktopPolicy.AutoScrollMinSize;
//
//            Invalidate();
//        }

        [DefaultValue(false)]
        [Description("Determines whether to use desktop scaling or leave it normal and clip")]
        /// <summary>
        /// Determines whether to use desktop scaling or leave it normal and clip.
        /// </summary>
        public bool Scaled
        {
            get { return false; }
            
        }

        [DefaultValue(0)]
        [Description("Sets the number of Bits Per Pixel for the Framebuffer--one of 8, 16, or 32")]
        /// <summary>
        /// Sets the number of Bits Per Pixel for the Framebuffer--one of 8, 16, or 32
        /// </summary>
        public int BitsPerPixel
        {
            get { return bitsPerPixel; }
            set { bitsPerPixel = value; }
        }

        [DefaultValue(0)]
        [Description("Sets the Colour Depth of the Framebuffer--one of 3, 6, 8, or 16")]
        /// <summary>
        /// Sets the Colour Depth of the Framebuffer--one of 3, 6, 8, or 16
        /// </summary>
        public int Depth
        {
            get { return depth; }
            set { depth = value; }
        }

        /// <summary>
        /// After protocol-level initialization and connecting is complete, the local GUI objects have to be set-up,
        /// and requests for updates to the remote host begun.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is already in the
        /// Connected state.  See <see cref="IsConnected" />.</exception>		
        private void Initialize()
        {
            // Finish protocol handshake with host now that authentication is done.
            InsureConnection(false);
            vnc.Initialize(bitsPerPixel, depth);
            SetState(RuntimeState.Connected);

            // Create a buffer on which updated rectangles will be drawn and draw a "please wait..." 
            // message on the buffer for initial display until we start getting rectangles
            SetupDesktop();

            // Tell the user of this control the necessary info about the desktop in order to setup the display
            OnConnectComplete(new ConnectEventArgs(vnc.Framebuffer.Width,
                vnc.Framebuffer.Height,
                vnc.Framebuffer.DesktopName));


            // Start getting updates from the remote host (vnc.StartUpdates will begin a worker thread).
            vnc.VncUpdate += VncUpdate;
            vnc.StartUpdates();
        }

        private void SetState(RuntimeState newState)
        {
            state = newState;
        }

        /// <summary>
        /// Creates and initially sets-up the local bitmap that will represent the remote desktop image.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not already in
        /// the Connected state. See <see cref="IsConnected" />.</exception>
        private void SetupDesktop()
        {
            InsureConnection(true);

            // Create a new bitmap to cache locally the remote desktop image.  Use the geometry of the
            // remote framebuffer, and 32bpp pixel format (doesn't matter what the server is sending--8,16,
            // or 32--we always draw 32bpp here for efficiency).
            desktop = new Bitmap(vnc.Framebuffer.Width, vnc.Framebuffer.Height, PixelFormat.Format32bppPArgb);
        }


        /// <summary>
        /// Stops the remote host from sending further updates and disconnects.
        /// </summary>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not already in
        /// the Connected state. See <see cref="IsConnected" />.</exception>
        public void Disconnect()
        {
            InsureConnection(true);
            vnc.ConnectionLost -= VncClientConnectionLost;
            vnc.ServerCutText -= VncServerCutText;
            vnc.Disconnect();
            SetState(RuntimeState.Disconnected);
            OnConnectionLost();
        }

        
        /// <summary>
        /// Fills the remote server's clipboard with text.
        /// </summary>
        /// <param name="text">The text to put in the server's clipboard.</param>
        
        private void FillServerClipboard(string text)
        {
            vnc.WriteClientCutText(text);
        }

        public void Dispose(bool disposing)
        {
            if (!disposing) return;
            // Make sure the connection is closed--should never happen :)
            if (state != RuntimeState.Disconnected)
                Disconnect();

            // See if either of the bitmaps used need clean-up.  
            // CodeAnalysis doesn't like null propagation...
            desktop?.Dispose();
        }
        
        /// <summary>
        /// RemoteDesktop listens for ConnectionLost events from the VncClient object.
        /// </summary>
        /// <param name="sender">The VncClient object that raised the event.</param>
        /// <param name="e">An empty EventArgs object.</param>
        private void VncClientConnectionLost(object sender, EventArgs e)
        {
            // If the remote host dies, and there are attempts to write
            // keyboard/mouse/update notifications, this may get called 
            // many times, and from main or worker thread.
            // Guard against this and invoke Disconnect once.
            if (state != RuntimeState.Connected) return;
            SetState(RuntimeState.Disconnecting);
            Disconnect();
        }

        // Handle the VncClient ServerCutText event and bubble it up as ClipboardChanged.
        private void VncServerCutText(object sender, EventArgs e)
        {
            OnClipboardChanged();
        }

        private void OnClipboardChanged()
        {
            ClipboardChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Dispatches the ConnectionLost event if any targets have registered.
        /// </summary>
        /// <param name="e">An EventArgs object.</param>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is in the
        /// Connected state.</exception>
        private void OnConnectionLost()
        {
            ConnectionLost?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Dispatches the ConnectComplete event if any targets have registered.
        /// </summary>
        /// <param name="e">A ConnectEventArgs object with information about the remote framebuffer's geometry.</param>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the
        /// Connected state.</exception>
        private void OnConnectComplete(ConnectEventArgs e)
        {
            ConnectComplete?.Invoke(this, e);
        }

        // KEY MANAGEMENT
        private static readonly Dictionary<int, int> KeyTranslationTable = new Dictionary<int, int>
        {
            {NativeMethods.VK_CANCEL, RfbProtocol.XK_Cancel},
            {NativeMethods.VK_BACK, RfbProtocol.XK_BackSpace},
            {NativeMethods.VK_TAB, RfbProtocol.XK_Tab},
            {NativeMethods.VK_CLEAR, RfbProtocol.XK_Clear},
            {NativeMethods.VK_RETURN, RfbProtocol.XK_Return},
            {NativeMethods.VK_PAUSE, RfbProtocol.XK_Pause},
            {NativeMethods.VK_ESCAPE, RfbProtocol.XK_Escape},
            {NativeMethods.VK_SNAPSHOT, RfbProtocol.XK_Sys_Req},
            {NativeMethods.VK_INSERT, RfbProtocol.XK_Insert},
            {NativeMethods.VK_DELETE, RfbProtocol.XK_Delete},
            {NativeMethods.VK_HOME, RfbProtocol.XK_Home},
            {NativeMethods.VK_END, RfbProtocol.XK_End},
            {NativeMethods.VK_PRIOR, RfbProtocol.XK_Prior}, // Page Up
            {NativeMethods.VK_NEXT, RfbProtocol.XK_Next}, // Page Down
            {NativeMethods.VK_LEFT, RfbProtocol.XK_Left},
            {NativeMethods.VK_UP, RfbProtocol.XK_Up},
            {NativeMethods.VK_RIGHT, RfbProtocol.XK_Right},
            {NativeMethods.VK_DOWN, RfbProtocol.XK_Down},
            {NativeMethods.VK_SELECT, RfbProtocol.XK_Select},
            {NativeMethods.VK_PRINT, RfbProtocol.XK_Print},
            {NativeMethods.VK_EXECUTE, RfbProtocol.XK_Execute},
            {NativeMethods.VK_HELP, RfbProtocol.XK_Help},
            {NativeMethods.VK_F1, RfbProtocol.XK_F1},
            {NativeMethods.VK_F2, RfbProtocol.XK_F2},
            {NativeMethods.VK_F3, RfbProtocol.XK_F3},
            {NativeMethods.VK_F4, RfbProtocol.XK_F4},
            {NativeMethods.VK_F5, RfbProtocol.XK_F5},
            {NativeMethods.VK_F6, RfbProtocol.XK_F6},
            {NativeMethods.VK_F7, RfbProtocol.XK_F7},
            {NativeMethods.VK_F8, RfbProtocol.XK_F8},
            {NativeMethods.VK_F9, RfbProtocol.XK_F9},
            {NativeMethods.VK_F10, RfbProtocol.XK_F10},
            {NativeMethods.VK_F11, RfbProtocol.XK_F11},
            {NativeMethods.VK_F12, RfbProtocol.XK_F12},
            {NativeMethods.VK_APPS, RfbProtocol.XK_Menu}
        };

        public static int TranslateVirtualKey(int virtualKey, KeyboardHook.ModifierKeys modifierKeys)
        {
            if (KeyTranslationTable.ContainsKey(virtualKey))
                return KeyTranslationTable[virtualKey];

            // Windows sends the uppercase letter when the user presses a hotkey
            // like Ctrl-A. ToAscii takes into effect the keyboard layout and
            // state of the modifier keys. This will give us the lowercase letter
            // unless the user is also pressing Shift.
            var keyboardState = new byte[256];
            if (!NativeMethods.GetKeyboardState(keyboardState))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            keyboardState[NativeMethods.VK_CONTROL] = 0;
            keyboardState[NativeMethods.VK_LCONTROL] = 0;
            keyboardState[NativeMethods.VK_RCONTROL] = 0;
            keyboardState[NativeMethods.VK_MENU] = 0;
            keyboardState[NativeMethods.VK_LMENU] = 0;
            keyboardState[NativeMethods.VK_RMENU] = 0;
            keyboardState[NativeMethods.VK_LWIN] = 0;
            keyboardState[NativeMethods.VK_RWIN] = 0;

            var charResult = new byte[2];
            var charCount = NativeMethods.ToAscii(virtualKey, NativeMethods.MapVirtualKey(virtualKey, 0), 
                keyboardState, charResult, 0);

            // TODO: This could probably be handled better. For now, we'll just return the last character.
            return charCount > 0 ? Convert.ToInt32(charResult[charCount - 1]) : virtualKey;
        }

        public static bool IsModifierKey(int keyCode)
        {
            switch (keyCode)
            {
                case NativeMethods.VK_SHIFT:
                case NativeMethods.VK_LSHIFT:
                case NativeMethods.VK_RSHIFT:
                case NativeMethods.VK_CONTROL:
                case NativeMethods.VK_LCONTROL:
                case NativeMethods.VK_RCONTROL:
                case NativeMethods.VK_MENU:
                case NativeMethods.VK_LMENU:
                case NativeMethods.VK_RMENU:
                case NativeMethods.VK_LWIN:
                case NativeMethods.VK_RWIN:
                    return true;
                default:
                    return false;
            }
        }

        private KeyboardHook.ModifierKeys PreviousModifierKeyState;

        private void SyncModifierKeyState(KeyboardHook.ModifierKeys modifierKeys)
        {
            if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.LeftShift) !=
                (modifierKeys & KeyboardHook.ModifierKeys.LeftShift))
                vnc.WriteKeyboardEvent(RfbProtocol.XK_Shift_L,
                    (modifierKeys & KeyboardHook.ModifierKeys.LeftShift) != 0);
            if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.RightShift) !=
                (modifierKeys & KeyboardHook.ModifierKeys.RightShift))
                vnc.WriteKeyboardEvent(RfbProtocol.XK_Shift_R,
                    (modifierKeys & KeyboardHook.ModifierKeys.RightShift) != 0);

            if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.LeftControl) !=
                (modifierKeys & KeyboardHook.ModifierKeys.LeftControl))
                vnc.WriteKeyboardEvent(RfbProtocol.XK_Control_L,
                    (modifierKeys & KeyboardHook.ModifierKeys.LeftControl) != 0);
            if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.RightControl) !=
                (modifierKeys & KeyboardHook.ModifierKeys.RightControl))
                vnc.WriteKeyboardEvent(RfbProtocol.XK_Control_R,
                    (modifierKeys & KeyboardHook.ModifierKeys.RightControl) != 0);

            if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.LeftAlt) !=
                (modifierKeys & KeyboardHook.ModifierKeys.LeftAlt))
                vnc.WriteKeyboardEvent(RfbProtocol.XK_Alt_L, (modifierKeys & KeyboardHook.ModifierKeys.LeftAlt) != 0);
            if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.RightAlt) !=
                (modifierKeys & KeyboardHook.ModifierKeys.RightAlt))
                vnc.WriteKeyboardEvent(RfbProtocol.XK_Alt_R, (modifierKeys & KeyboardHook.ModifierKeys.RightAlt) != 0);

            if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.LeftWin) !=
                (modifierKeys & KeyboardHook.ModifierKeys.LeftWin))
                vnc.WriteKeyboardEvent(RfbProtocol.XK_Super_L, (modifierKeys & KeyboardHook.ModifierKeys.LeftWin) != 0);
            if ((PreviousModifierKeyState & KeyboardHook.ModifierKeys.RightWin) !=
                (modifierKeys & KeyboardHook.ModifierKeys.RightWin))
                vnc.WriteKeyboardEvent(RfbProtocol.XK_Super_R, 
                    (modifierKeys & KeyboardHook.ModifierKeys.RightWin) != 0);

            PreviousModifierKeyState = modifierKeys;
        }

        private bool HandleKeyboardEvent(int msg, int virtualKey, KeyboardHook.ModifierKeys modifierKeys)
        {
            if (DesignMode || !IsConnected)
                return false;

            if (modifierKeys != PreviousModifierKeyState)
                SyncModifierKeyState(modifierKeys);

            if (IsModifierKey(virtualKey)) return true;

            bool pressed;
            switch (msg)
            {
                case NativeMethods.WM_KEYDOWN:
                case NativeMethods.WM_SYSKEYDOWN:
                    pressed = true;
                    break;
                case NativeMethods.WM_KEYUP:
                case NativeMethods.WM_SYSKEYUP:
                    pressed = false;
                    break;
                default:
                    return false;
            }

            vnc.WriteKeyboardEvent(Convert.ToUInt32(TranslateVirtualKey(virtualKey, modifierKeys)), pressed);

            return true;
        }

        /// <summary>
        /// Sends a keyboard combination that would otherwise be reserved for the client PC.
        /// </summary>
        /// <param name="keys">SpecialKeys is an enumerated list of supported keyboard combinations.</param>
        /// <remarks>Keyboard combinations are Pressed and then Released, while single keys (e.g., SpecialKeys.Ctrl)
        /// are only pressed so that subsequent keys will be modified.</remarks>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the
        /// Connected state.</exception>
        public void SendSpecialKeys(SpecialKeys keys)
        {
            SendSpecialKeys(keys, true);
        }

        /// <summary>
        /// Sends a keyboard combination that would otherwise be reserved for the client PC.
        /// </summary>
        /// <param name="keys">SpecialKeys is an enumerated list of supported keyboard combinations.</param>
        /// /// <param name="release">Boolean release</param>
        /// <remarks>Keyboard combinations are Pressed and then Released, while single keys (e.g., SpecialKeys.Ctrl)
        /// are only pressed so that subsequent keys will be modified.</remarks>
        /// <exception cref="System.InvalidOperationException">Thrown if the RemoteDesktop control is not in the
        /// Connected state.</exception>
        private void SendSpecialKeys(SpecialKeys keys, bool release)
        {
            InsureConnection(true);
            // For all of these I am sending the key presses manually instead of calling
            // the keyboard event handlers, as I don't want to propegate the calls up to the 
            // base control class and form.
            // ReSharper disable once SwitchStatementMissingSomeCases
            switch (keys)
            {
                case SpecialKeys.Ctrl:
                    PressKeys(new uint[] {0xffe3}, release); // CTRL, but don't release
                    break;
                case SpecialKeys.Alt:
                    PressKeys(new uint[] {0xffe9}, release); // ALT, but don't release
                    break;
                case SpecialKeys.CtrlAltDel:
                    PressKeys(new uint[] {0xffe3, 0xffe9, 0xffff}, release); // CTRL, ALT, DEL
                    break;
                case SpecialKeys.AltF4:
                    PressKeys(new uint[] {0xffe9, 0xffc1}, release); // ALT, F4
                    break;
                case SpecialKeys.CtrlEsc:
                    PressKeys(new uint[] {0xffe3, 0xff1b}, release); // CTRL, ESC
                    break;
                // TODO: are there more I should support???
            }
        }

        /// <summary>
        /// Given a list of keysym values, sends a key press for each, then a release.
        /// </summary>
        /// <param name="keys">An array of keysym values representing keys to press/release.</param>
        /// <param name="release">A boolean indicating whether the keys should be Pressed and then Released.</param>
        private void PressKeys(uint[] keys, bool release)
        {
            Debug.Assert(keys != null, "keys[] cannot be null.");

            foreach (var u in keys)
                vnc.WriteKeyboardEvent(u, true);

            if (!release) return;

            // Walk the keys array backwards in order to release keys in correct order
            for (var i = keys.Length - 1; i >= 0; --i)
                vnc.WriteKeyboardEvent(keys[i], false);
        }
    }
}
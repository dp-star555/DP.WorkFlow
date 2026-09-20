using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ModernUI.WinForms;

namespace DP.WorkFlow.Tests;

public sealed class FeedbackLifecycleTests
{
    [Fact]
    public void DuplicateFeedbackIsMergedAndRestartsTheExistingSurface()
    {
        RunInSta(() =>
        {
            var previousMerge = ModernUiSettings.MergeDuplicateFeedback;
            try
            {
                ModernUiSettings.MergeDuplicateFeedback = true;
                using var owner = CreateOwner();
                using var firstMessage = ModernMessage.Success(owner, "Saved", 0);
                using var duplicateMessage = ModernMessage.Success(owner, "Saved", 0);
                using var firstNotification = ModernNotification.Show(owner, "Complete", "No errors.", ModernVisualStatus.Success, 0);
                using var duplicateNotification = ModernNotification.Show(owner, "Complete", "No errors.", ModernVisualStatus.Success, 0);

                Assert.Same(firstMessage, duplicateMessage);
                Assert.Same(firstNotification, duplicateNotification);
                Assert.False(((Form)firstMessage).IsDisposed);
                Assert.False(((Form)firstNotification).IsDisposed);
            }
            finally { ModernUiSettings.MergeDuplicateFeedback = previousMerge; }
        });
    }

    [Fact]
    public void FeedbackUsesDeterministicLogicalTypographyAndGeometry()
    {
        RunInSta(() =>
        {
            using var owner = CreateOwner();
            using var message = ModernMessage.Info(owner, "运行命令已执行", 0);
            using var notification = ModernNotification.Show(owner, "诊断完成", "发现 2 条可优化建议。", duration: 0);

            var messageWindow = (Form)message;
            var notificationWindow = (Form)notification;
            Assert.Equal("Microsoft YaHei UI", messageWindow.Font.FontFamily.Name);
            Assert.Equal(9F, messageWindow.Font.SizeInPoints);
            Assert.Equal("Microsoft YaHei UI", notificationWindow.Font.FontFamily.Name);
            Assert.Equal(9F, notificationWindow.Font.SizeInPoints);
            int Scale(int logical) => (int)Math.Round(logical * owner.DeviceDpi / 96d);
            Assert.Equal(Scale(46), messageWindow.ClientSize.Height);
            var alert = messageWindow.Controls.OfType<ModernAlert>().Single();
            using var bitmap = new Bitmap(alert.Width, alert.Height);
            alert.DrawToBitmap(bitmap, alert.ClientRectangle);
            var textInk = Enumerable.Range(0, bitmap.Width)
                .SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => (X: x, Color: bitmap.GetPixel(x, y))))
                .Where(pixel => pixel.X > Scale(30) && pixel.Color.R < 90 && pixel.Color.G < 110 && pixel.Color.B < 130)
                .Select(pixel => pixel.X)
                .DefaultIfEmpty(-1)
                .Max();
            Assert.True(textInk < alert.Width - Scale(28), $"Message text reaches close-button chrome at x={textInk}, width={alert.Width}.");
            Assert.Equal(Scale(340), notificationWindow.ClientSize.Width);
            Assert.Equal(Scale(88), notificationWindow.ClientSize.Height);
        });
    }

    [Fact]
    public void MessageWindowFitsCompleteLongTextInsideCloseButtonSafeArea()
    {
        RunInSta(() =>
        {
            using var owner = CreateOwner();
            const string text = "这是一条全局消息，用于确认完整真实文本不会被关闭按钮或窗口边界裁剪";
            using var message = ModernMessage.Info(owner, text, 0);
            var window = (Form)message;
            var alert = window.Controls.OfType<ModernAlert>().Single();
            using var titleFont = new Font(alert.Font, FontStyle.Bold);
            var textLeft = alert.Padding.Left + ScaleFor(owner, 18 + 9);
            var closeLeft = alert.Width - alert.Padding.Right - ScaleFor(owner, 22);
            var available = Math.Max(1, closeLeft - textLeft);
            var measured = TextRenderer.MeasureText(text, titleFont, new Size(available, int.MaxValue),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            var verticalAvailable = alert.Height - alert.Padding.Vertical;

            Assert.True(measured.Width <= available, $"Message text width {measured.Width} exceeds {available}.");
            Assert.True(measured.Height <= verticalAvailable,
                $"Message text height {measured.Height} exceeds {verticalAvailable}; window={window.ClientSize}.");
        });
    }

    [Fact]
    public void ApplyThemeCoversNativeBackedModernControlsThroughSharedAdapter()
    {
        RunInSta(() =>
        {
            using var root = new Panel();
            using var splitter = new ModernSplitter();
            using var tabs = new ModernTabControl();
            using var list = new ModernListView();
            using var propertyGrid = new ModernPropertyGrid.WinForms.ModernPropertyGrid();
            root.Controls.AddRange([splitter, tabs, list, propertyGrid]);

            ModernUiSettings.ApplyTheme(root, ModernTheme.Dark);

            Assert.Same(ModernTheme.Dark, splitter.Theme);
            Assert.Same(ModernTheme.Dark, tabs.Theme);
            Assert.Same(ModernTheme.Dark, list.Theme);
            Assert.Same(ModernTheme.Dark, propertyGrid.Theme);
        });
    }

    [Fact]
    public void FeedbackResolvesThemeFromNativeBackedModernOwnerControls()
    {
        RunInSta(() =>
        {
            using var owner = CreateOwner();
            using var tabs = new ModernTabControl { Dock = DockStyle.Fill, Theme = ModernTheme.Dark };
            tabs.TabPages.Add(new TabPage("Trace"));
            owner.Controls.Add(tabs);

            using var notification = ModernNotification.Show(owner, "Theme resolved", "From tabs", duration: 0);
            var alert = ((Form)notification).Controls.OfType<ModernAlert>().Single();

            Assert.Same(ModernTheme.Dark, alert.Theme);
        });
    }

    [Fact]
    public void NotificationTopLevelWindowExposesAlertNameDescriptionAndAction()
    {
        RunInSta(() =>
        {
            var command = new ModernCommand(() => { })
            {
                Text = "Open details",
                Description = "Open workflow details"
            };
            using var owner = CreateOwner();
            using var notification = ModernNotification.Show(owner, new ModernNotificationOptions
            {
                Title = "DP.WorkFlow complete",
                Description = "No errors were found.",
                Duration = 0,
                ActionCommand = command
            });
            var window = (Form)notification;
            var accessibility = window.AccessibilityObject;

            Assert.Equal(AccessibleRole.Alert, accessibility.Role);
            Assert.Equal("DP.WorkFlow complete", accessibility.Name);
            Assert.Equal("No errors were found.", accessibility.Description);
            var action = window.Controls.OfType<ModernButton>().Single();
            Assert.Equal("Open details", action.AccessibilityObject.Name);
            Assert.Equal("Open workflow details", action.AccessibilityObject.Description);
        });
    }

    [Fact]
    public void OpenFeedbackTracksOwnerThemeChanges()
    {
        RunInSta(() =>
        {
            using var owner = CreateOwner();
            owner.Controls.Add(new ModernPanel { Dock = DockStyle.Fill, Theme = ModernTheme.Light });
            using var message = ModernMessage.Info(owner, "Changed", 0);
            using var notification = ModernNotification.Show(owner, "Changed", "Theme", ModernVisualStatus.Success, 0);

            ModernUiSettings.ApplyTheme(owner, ModernTheme.Dark);

            var messageAlert = ((Form)message).Controls.OfType<ModernAlert>().Single();
            var notificationAlert = ((Form)notification).Controls.OfType<ModernAlert>().Single();
            Assert.Same(ModernTheme.Dark, messageAlert.Theme);
            Assert.Same(ModernTheme.Dark, notificationAlert.Theme);
            var messageSurface = Color.FromArgb(17, 26, 44);
            var notificationSurface = Color.FromArgb(22, 35, 18);
            Assert.Equal(messageSurface, ((Form)message).BackColor);
            Assert.Equal(notificationSurface, ((Form)notification).BackColor);
            using var bitmap = new Bitmap(notificationAlert.Width, notificationAlert.Height);
            notificationAlert.DrawToBitmap(bitmap, notificationAlert.ClientRectangle);
            Assert.Equal(notificationSurface, bitmap.GetPixel(notificationAlert.Width / 2, notificationAlert.Height - 6));
        });
    }

    [Fact]
    public void AutoClosePreservesRemainingTimeWhileOwnerIsMinimized()
    {
        RunInSta(() =>
        {
            var previousAnimations = ModernUiSettings.AnimationsEnabled;
            try
            {
                ModernUiSettings.AnimationsEnabled = false;
                using var owner = CreateOwner();
                using var message = ModernMessage.Info(owner, "Waiting", 240);
                PumpFor(110);
                owner.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                PumpFor(300);
                Assert.False(((Form)message).IsDisposed);

                owner.WindowState = FormWindowState.Normal;
                Application.DoEvents();
                PumpFor(170);
                Assert.True(((Form)message).IsDisposed);
            }
            finally { ModernUiSettings.AnimationsEnabled = previousAnimations; }
        });
    }

    [Fact]
    public void NotificationActionUsesSharedCommandAndCanCloseTheSurface()
    {
        RunInSta(() =>
        {
            var previousAnimations = ModernUiSettings.AnimationsEnabled;
            try
            {
                ModernUiSettings.AnimationsEnabled = false;
                var executions = 0;
                var command = new ModernCommand(() => executions++)
                {
                    Text = "Open details",
                    Description = "Open workflow details",
                    Icon = ModernIconKind.Info
                };
                using var owner = CreateOwner();
                using var notification = ModernNotification.Show(owner, new ModernNotificationOptions
                {
                    Title = "DP.WorkFlow completed",
                    Description = "No errors were found.",
                    Status = ModernVisualStatus.Success,
                    Duration = 0,
                    ActionCommand = command,
                    CloseOnAction = true
                });
                var window = (Form)notification;
                var action = window.Controls.OfType<ModernButton>().Single();
                Assert.Same(command, action.Command);
                Assert.Equal(AccessibleRole.PushButton, action.AccessibilityObject.Role);
                Assert.Equal("Open workflow details", action.AccessibleDescription);

                action.PerformClick();
                Application.DoEvents();
                Assert.Equal(1, executions);
                Assert.True(window.IsDisposed);
            }
            finally { ModernUiSettings.AnimationsEnabled = previousAnimations; }
        });
    }

    [Fact]
    public void LongFeedbackWrapsAndNotificationQueueFitsOwnerHeight()
    {
        RunInSta(() =>
        {
            var previousMerge = ModernUiSettings.MergeDuplicateFeedback;
            var previousNotifications = ModernUiSettings.MaximumVisibleNotifications;
            var leases = new List<IDisposable>();
            try
            {
                ModernUiSettings.MergeDuplicateFeedback = false;
                ModernUiSettings.MaximumVisibleNotifications = 5;
                using var owner = new Form
                {
                    StartPosition = FormStartPosition.Manual,
                    Bounds = new Rectangle(120, 120, 760, 280)
                };
                owner.Show();
                var longText = string.Join(" ", Enumerable.Repeat("A long workflow message that must remain readable.", 12));
                var message = ModernMessage.Info(owner, longText, 0);
                leases.Add(message);
                Assert.True(((Form)message).ClientSize.Height > 46);
                Assert.True(((Form)message).Controls.OfType<ModernAlert>().Single().WrapText);

                for (var index = 0; index < 4; index++)
                    leases.Add(ModernNotification.Show(owner, $"Notification {index}",
                        "This description wraps without leaving the owner client area.", duration: 0));
                Application.DoEvents();
                var liveNotifications = leases.Skip(1).Cast<Form>().Where(window => !window.IsDisposed).ToArray();
                Assert.NotEmpty(liveNotifications);
                Assert.True(liveNotifications.Length < 4);
                var ownerBounds = owner.RectangleToScreen(owner.ClientRectangle);
                Assert.All(liveNotifications, window =>
                {
                    Assert.True(window.Top >= ownerBounds.Top);
                    Assert.True(window.Bottom <= ownerBounds.Bottom);
                    Assert.True(window.Controls.OfType<ModernAlert>().Single().WrapText);
                });
            }
            finally
            {
                foreach (var lease in leases) lease.Dispose();
                ModernUiSettings.MergeDuplicateFeedback = previousMerge;
                ModernUiSettings.MaximumVisibleNotifications = previousNotifications;
            }
        });
    }

    [Fact]
    public void RepeatedFeedbackCreationReleasesOwnedWindowsAndHandles()
    {
        RunInSta(() =>
        {
            var previousAnimations = ModernUiSettings.AnimationsEnabled;
            try
            {
                ModernUiSettings.AnimationsEnabled = false;
                using var owner = CreateOwner();
                for (var index = 0; index < 4; index++)
                {
                    using var warmupMessage = ModernMessage.Info(owner, $"Warmup message {index}", 0);
                    using var warmupNotification = ModernNotification.Show(owner, $"Warmup notification {index}", "Body", duration: 0);
                }
                Application.DoEvents();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                var process = Process.GetCurrentProcess();
                process.Refresh();
                var baselineHandles = process.HandleCount;
                var baselineGdi = GetGuiResources(process.Handle, 0);
                var baselineUser = GetGuiResources(process.Handle, 1);
                for (var index = 0; index < 120; index++)
                {
                    using var message = ModernMessage.Info(owner, $"Message {index}", 0);
                    using var notification = ModernNotification.Show(owner, $"Notification {index}", "Body", duration: 0);
                    if (index % 10 == 0) Application.DoEvents();
                }
                Application.DoEvents();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Application.DoEvents();
                process.Refresh();

                Assert.Empty(owner.OwnedForms);
                Assert.InRange(process.HandleCount - baselineHandles, int.MinValue, 24);
                Assert.InRange((int)GetGuiResources(process.Handle, 0) - (int)baselineGdi, int.MinValue, 8);
                Assert.InRange((int)GetGuiResources(process.Handle, 1) - (int)baselineUser, int.MinValue, 8);
            }
            finally { ModernUiSettings.AnimationsEnabled = previousAnimations; }
        });
    }

    [Fact]
    public void FeedbackQueuesDisposeTheOldestSurfaceAtTheirConfiguredLimit()
    {
        RunInSta(() =>
        {
            var previousMerge = ModernUiSettings.MergeDuplicateFeedback;
            var previousMessages = ModernUiSettings.MaximumVisibleMessages;
            var previousNotifications = ModernUiSettings.MaximumVisibleNotifications;
            var leases = new List<IDisposable>();
            try
            {
                ModernUiSettings.MergeDuplicateFeedback = false;
                ModernUiSettings.MaximumVisibleMessages = 2;
                ModernUiSettings.MaximumVisibleNotifications = 2;
                using var owner = CreateOwner();
                var oldestMessage = ModernMessage.Info(owner, "Message 1", 0);
                leases.Add(oldestMessage);
                leases.Add(ModernMessage.Info(owner, "Message 2", 0));
                leases.Add(ModernMessage.Info(owner, "Message 3", 0));
                var oldestNotification = ModernNotification.Show(owner, "Notification 1", "Body", duration: 0);
                leases.Add(oldestNotification);
                leases.Add(ModernNotification.Show(owner, "Notification 2", "Body", duration: 0));
                leases.Add(ModernNotification.Show(owner, "Notification 3", "Body", duration: 0));

                Assert.True(((Form)oldestMessage).IsDisposed);
                Assert.True(((Form)oldestNotification).IsDisposed);
                Assert.False(((Form)leases[2]).IsDisposed);
                Assert.False(((Form)leases[5]).IsDisposed);
            }
            finally
            {
                foreach (var lease in leases) lease.Dispose();
                ModernUiSettings.MergeDuplicateFeedback = previousMerge;
                ModernUiSettings.MaximumVisibleMessages = previousMessages;
                ModernUiSettings.MaximumVisibleNotifications = previousNotifications;
            }
        });
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    private static Form CreateOwner()
    {
        var owner = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(120, 120, 760, 480)
        };
        owner.Show();
        return owner;
    }

    private static int ScaleFor(Control owner, int logical) =>
        (int)Math.Round(logical * owner.DeviceDpi / 96d);

    private static void PumpFor(int milliseconds)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    private static void RunInSta(Action action)
    {
        Exception? failure = null;
        var thread = UiTestThread.Create(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "Feedback lifecycle test timed out.");
        Assert.Null(failure);
    }
}

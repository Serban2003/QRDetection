using OpenCvSharp; // Core OpenCV functions
using OpenCvSharp.WpfExtensions; // Conversion between OpenCV images and WPF images
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel; // Used for data-binding with the UI
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace QRDetection
{
    /// <summary>
    /// Main window class of the QR detection application
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private VideoCapture capture; // Handles video input from the camera
        private CancellationTokenSource cts; // Used to cancel the camera task when the app closes
        private QRCodeDetector qrDecoder; // OpenCV QR code detector

        private ObservableCollection<QRCode> qrEntries = new ObservableCollection<QRCode>(); // Stores detected QR codes for UI binding
        private HashSet<string> recentQRCodes = new HashSet<string>(); // Prevents duplicate entries
        private object qrLock = new object(); // Synchronization object for thread safety

        public MainWindow()
        {
            InitializeComponent(); // Initialize UI components
            QrListBox.ItemsSource = qrEntries; // Bind the QR list to the UI list box
            Loaded += MainWindow_Loaded; // Event for when the window is loaded
            Closing += MainWindow_Closing; // Event for when the window is closing
        }

        // Called when the window finishes loading
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            cts = new CancellationTokenSource(); // Prepare to manage cancellation of camera task
            qrDecoder = new QRCodeDetector(); // Initialize the QR code detector
            StartCameraAsync(cts.Token); // Start reading from the camera
        }

        // Asynchronous method that runs the camera capture and QR detection
        private async Task StartCameraAsync(CancellationToken token)
        {
            capture = new VideoCapture(0); // Use default webcam
            if (!capture.IsOpened())
            {
                MessageBox.Show("Failed to open camera.");
                return;
            }

            var frame = new Mat(); // Holds a frame from the camera
            var newQRCodes = new List<QRCode>(); // Temporarily holds newly detected QR codes

            // Run camera reading and QR detection on a separate thread
            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    capture.Read(frame); // Read a frame
                    if (!frame.Empty())
                    {
                        Point2f[] pointsArray;
                        bool success = qrDecoder.DetectMulti(frame, out pointsArray); // Detect multiple QR codes

                        if (success && pointsArray.Length % 4 == 0)
                        {
                            int qrCount = pointsArray.Length / 4;
                            var pointsList = new Point2f[qrCount][];

                            for (int i = 0; i < qrCount; i++)
                            {
                                var points = new Point2f[4];
                                Array.Copy(pointsArray, i * 4, points, 0, 4);
                                pointsList[i] = points;

                                string decoded = qrDecoder.Decode(frame, points); // Try to decode each QR code
                                if (!string.IsNullOrWhiteSpace(decoded))
                                {
                                    lock (qrLock) // Ensure thread-safe access
                                    {
                                        if (recentQRCodes.Add(decoded)) // If it's a new QR code
                                        {
                                            newQRCodes.Add(new QRCode
                                            {
                                                Content = decoded,
                                                Timestamp = DateTime.Now
                                            });
                                        }
                                    }
                                }

                                // Draw the detected QR code border in red
                                for (int j = 0; j < 4; j++)
                                {
                                    Cv2.Line(frame, (OpenCvSharp.Point)points[j], (OpenCvSharp.Point)points[(j + 1) % 4], Scalar.Red, 2);
                                }
                            }
                        }

                        // Convert the frame to a WPF-compatible image and freeze it for UI thread use
                        var image = frame.ToWriteableBitmap();
                        image.Freeze();

                        // Update UI on the main thread
                        Dispatcher.Invoke(() =>
                        {
                            CameraImage.Source = image;

                            // Add new QR codes to the UI list
                            foreach (var qr in newQRCodes)
                            {
                                qrEntries.Add(qr);
                            }
                            newQRCodes.Clear();
                        });
                    }

                    await Task.Delay(60); // Small delay to match ~30 FPS
                }
            });
        }

        // Clean up when the window is closing
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            cts?.Cancel(); // Stop the background task
            capture?.Release(); // Release the camera
            capture?.Dispose(); // Dispose camera resources
        }

        // Copy the selected QR code content to the clipboard
        private void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (QrListBox.SelectedItem is QRCode selected)
            {
                Clipboard.SetText(selected.Content);
            }
        }

        // Clear all QR code entries from the list and memory
        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            qrEntries.Clear();
            recentQRCodes.Clear();
        }
    }
}

using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;



namespace QRDetection
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private VideoCapture capture;
        private CancellationTokenSource cts;
        private QRCodeDetector qrDecoder;

        private ObservableCollection<QRCode> qrEntries = new ObservableCollection<QRCode>();
        private HashSet<string> recentQRCodes = new HashSet<string>();
        private object qrLock = new object(); // for thread safety

        public MainWindow()
        {
            InitializeComponent();
            QrListBox.ItemsSource = qrEntries;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            cts = new CancellationTokenSource();
            qrDecoder = new QRCodeDetector();
            StartCameraAsync(cts.Token);
        }

        private async Task StartCameraAsync(CancellationToken token)
        {
            capture = new VideoCapture(0);
            if (!capture.IsOpened())
            {
                MessageBox.Show("Failed to open camera.");
                return;
            }

            var frame = new Mat();
            var newQRCodes = new List<QRCode>();

            await Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    capture.Read(frame);
                    if (!frame.Empty())
                    {
                        Point2f[] pointsArray;
                        bool success = qrDecoder.DetectMulti(frame, out pointsArray);

                        if (success && pointsArray.Length % 4 == 0)
                        {
                            int qrCount = pointsArray.Length / 4;
                            var pointsList = new Point2f[qrCount][];

                            for (int i = 0; i < qrCount; i++)
                            {
                                var points = new Point2f[4];
                                Array.Copy(pointsArray, i * 4, points, 0, 4);
                                pointsList[i] = points;

                                string decoded = qrDecoder.Decode(frame, points);
                                if (!string.IsNullOrWhiteSpace(decoded))
                                {
                                    lock (qrLock)
                                    {
                                        if (recentQRCodes.Add(decoded)) // Avoid duplicate
                                        {
                                            newQRCodes.Add(new QRCode
                                            {
                                                Content = decoded,
                                                Timestamp = DateTime.Now
                                            });
                                        }
                                    }
                                }

                                // Draw QR border
                                for (int j = 0; j < 4; j++)
                                {
                                    Cv2.Line(frame, (OpenCvSharp.Point)points[j], (OpenCvSharp.Point)points[(j + 1) % 4], Scalar.Red, 2);
                                }
                            }
                        }

                        // Update camera image
                        var image = frame.ToWriteableBitmap();
                        image.Freeze();

                        Dispatcher.Invoke(() =>
                        {
                            CameraImage.Source = image;

                            foreach (var qr in newQRCodes)
                            {
                                qrEntries.Add(qr);
                            }
                            newQRCodes.Clear();
                        });
                    }

                    await Task.Delay(60); // ~30 FPS
                }
            });
        }


        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            cts?.Cancel();
            capture?.Release();
            capture?.Dispose();
        }

        private void CopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (QrListBox.SelectedItem is QRCode selected)
            {
                Clipboard.SetText(selected.Content);
            }
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            qrEntries.Clear();
            recentQRCodes.Clear();
        }

    }
}

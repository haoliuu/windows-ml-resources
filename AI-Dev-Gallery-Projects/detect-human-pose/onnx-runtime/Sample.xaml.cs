using AIDevGallery.Sample.Utils;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    // TODO: Set this to the path of your local HRNet pose ONNX model file.
    private static readonly string ModelPath = @"<YOUR_ONNX_MODEL_PATH>";

    private InferenceSession? _inferenceSession;
    public Sample()
    {
        this.Unloaded += (s, e) => _inferenceSession?.Dispose();
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        try
        {
            await InitModel(ModelPath, ExecutionProviderDevicePolicy.DEFAULT, null, false, null);
            App.Window?.ModelLoaded();
        }
        catch (Exception ex)
        {
            App.Window?.ShowException(ex, "Failed to load model.");
            return;
        }

        await DetectPose(Path.Join(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "pose_default.png"));
    }

    private Task InitModel(string modelPath, ExecutionProviderDevicePolicy? policy, string? epName, bool compileModel, string? deviceType)
    {
        return Task.Run(async () =>
        {
            if (_inferenceSession != null)
            {
                return;
            }

            var catalog = Microsoft.Windows.AI.MachineLearning.ExecutionProviderCatalog.GetDefault();

            try
            {
                var registeredProviders = await catalog.EnsureAndRegisterCertifiedAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WARNING: Failed to install packages: {ex.Message}");
            }

            SessionOptions sessionOptions = new();
            sessionOptions.RegisterOrtExtensions();

            if (policy != null)
            {
                sessionOptions.SetEpSelectionPolicy(policy.Value);
            }
            else if (epName != null)
            {
                sessionOptions.AppendExecutionProviderFromEpName(epName, deviceType);

                if (compileModel)
                {
                    modelPath = sessionOptions.GetCompiledModel(modelPath, epName) ?? modelPath;
                }
            }

            _inferenceSession = new InferenceSession(modelPath, sessionOptions);
        });
    }

    private async void UploadButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new Window();
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".jpg");

        picker.ViewMode = PickerViewMode.Thumbnail;

        var file = await picker.PickSingleFileAsync();
        UploadButton.Focus(FocusState.Programmatic);
        if (file != null)
        {
            await DetectPose(file.Path);
        }
    }

    private async Task DetectPose(string filePath)
    {
        if (!Path.Exists(filePath))
        {
            return;
        }

        Loader.IsActive = true;
        Loader.Visibility = Visibility.Visible;
        UploadButton.Visibility = Visibility.Collapsed;
        DefaultImage.Source = new BitmapImage(new Uri(filePath));

        using Bitmap originalImage = new(filePath);

        int modelInputWidth = 256;
        int modelInputHeight = 192;

        using Bitmap resizedImage = BitmapFunctions.ResizeBitmap(originalImage, modelInputWidth, modelInputHeight);

        var predictions = await Task.Run(() =>
        {
            Tensor<float> input = new DenseTensor<float>([1, 3, modelInputWidth, modelInputHeight]);
            input = BitmapFunctions.PreprocessBitmapWithStdDev(resizedImage, input);

            var inputMetadataName = _inferenceSession!.InputNames[0];

            var onnxInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputMetadataName, input)
            };

            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _inferenceSession!.Run(onnxInputs);
            var heatmaps = results[0].AsTensor<float>();

            var outputName = _inferenceSession!.OutputNames[0];
            var outputDimensions = _inferenceSession!.OutputMetadata[outputName].Dimensions;

            float outputWidth = outputDimensions[2];
            float outputHeight = outputDimensions[3];

            List<(float X, float Y)> keypointCoordinates = PoseHelper.PostProcessResults(heatmaps, originalImage.Width, originalImage.Height, outputWidth, outputHeight);
            return keypointCoordinates;
        });

        using Bitmap output = PoseHelper.RenderPredictions(originalImage, predictions, .02f);
        BitmapImage outputImage = BitmapFunctions.ConvertBitmapToBitmapImage(output);

        DispatcherQueue.TryEnqueue(() =>
        {
            DefaultImage.Source = outputImage;
            Loader.IsActive = false;
            Loader.Visibility = Visibility.Collapsed;
            UploadButton.Visibility = Visibility.Visible;
        });
    }
}
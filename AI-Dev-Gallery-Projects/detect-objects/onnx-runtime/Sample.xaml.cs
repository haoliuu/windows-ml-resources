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
    // TODO: Set this to the path of your local YOLOv4 ONNX model file.
    private static readonly string ModelPath = @"<YOUR_ONNX_MODEL_PATH>";

    private InferenceSession? _inferenceSession;

    public Sample()
    {
        this.Unloaded += (s, e) => _inferenceSession?.Dispose();

        this.InitializeComponent();
    }

    private void Page_Loaded()
    {
        UploadButton.Focus(FocusState.Programmatic);
    }

    // </exclude>
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

        // Loads inference on default image
        await DetectObjects(Path.Join(Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "Assets", "team.jpg"));
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

        // Create a FileOpenPicker
        var picker = new FileOpenPicker();

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        // Set the file type filter
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".bmp");

        picker.ViewMode = PickerViewMode.Thumbnail;

        // Pick a file
        var file = await picker.PickSingleFileAsync();
        UploadButton.Focus(FocusState.Programmatic);
        if (file != null)
        {
            // Call function to run inference and classify image
            await DetectObjects(file.Path);
        }
    }

    private async Task DetectObjects(string filePath)
    {
        if (_inferenceSession == null)
        {
            return;
        }

        Loader.IsActive = true;
        Loader.Visibility = Visibility.Visible;
        UploadButton.Visibility = Visibility.Collapsed;

        DefaultImage.Source = new BitmapImage(new Uri(filePath));

        Bitmap image = new(filePath);

        int originalWidth = image.Width;
        int originalHeight = image.Height;

        var predictions = await Task.Run(() =>
        {
            // Set up
            var inputName = _inferenceSession.InputNames[0];
            var inputDimensions = _inferenceSession.InputMetadata[inputName].Dimensions;

            // Set batch size
            int batchSize = 1;
            inputDimensions[0] = batchSize;

            // I know the input dimensions to be [batchSize, 416, 416, 3]
            int inputWidth = inputDimensions[1];
            int inputHeight = inputDimensions[2];

            using var resizedImage = BitmapFunctions.ResizeWithPadding(image, inputWidth, inputHeight);

            // Preprocessing
            Tensor<float> input = new DenseTensor<float>(inputDimensions);
            input = BitmapFunctions.PreprocessBitmapForYOLO(resizedImage, input);

            // Setup inputs and outputs
            var inputMetadataName = _inferenceSession!.InputNames[0];
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputMetadataName, input)
            };

            // Run inference
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _inferenceSession!.Run(inputs);

            // Extract tensors from inference results
            var outputTensor1 = results[0].AsTensor<float>();
            var outputTensor2 = results[1].AsTensor<float>();
            var outputTensor3 = results[2].AsTensor<float>();

            // Define anchors (as per your model)
            var anchors = new List<(float Width, float Height)>
            {
                (12, 16), (19, 36), (40, 28),   // Small grid (52x52)
                (36, 75), (76, 55), (72, 146),  // Medium grid (26x26)
                (142, 110), (192, 243), (459, 401) // Large grid (13x13)
            };

            // Combine tensors into a list for processing
            var gridTensors = new List<Tensor<float>> { outputTensor1, outputTensor2, outputTensor3 };

            // Postprocessing steps
            var extractedPredictions = YOLOHelpers.ExtractPredictions(gridTensors, anchors, inputWidth, inputHeight, originalWidth, originalHeight);
            var filteredPredictions = YOLOHelpers.ApplyNms(extractedPredictions, .4f);

            // Return the final predictions
            return filteredPredictions;
        });

        BitmapImage outputImage = BitmapFunctions.RenderPredictions(image, predictions);

        DispatcherQueue.TryEnqueue(() =>
        {
            DefaultImage.Source = outputImage;
            Loader.IsActive = false;
            Loader.Visibility = Visibility.Collapsed;
            UploadButton.Visibility = Visibility.Visible;
        });

        image.Dispose();
    }
}
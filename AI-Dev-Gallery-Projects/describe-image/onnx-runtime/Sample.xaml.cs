using AIDevGallery.Sample.Utils;
using Microsoft.ML.OnnxRuntimeGenAI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private Model? model;
    private MultiModalProcessor? processor;
    private TokenizerStream? tokenizerStream;
    private CancellationTokenSource? _cts;
    private StorageFile? imageFile;

    // TODO: Set this to the path of your local ONNX vision model directory.
    // Example: @"C:\Models\Phi-3.5-vision-instruct-onnx\cpu-int4-rtn-block-32-acc-level-4"
    private static readonly string ModelPath = @"<YOUR_ONNX_MODEL_PATH>";

    public Sample()
    {
        this.InitializeComponent();

        this.Unloaded += (sender, args) => Dispose();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        await InitModel(ModelPath, CancellationToken.None);
        App.Window?.ModelLoaded();

    }

    private async Task InitModel(string modelPath, CancellationToken ct)
    {
        await Task.Run(
            () =>
            {
                try
                {
                    model = new Model(modelPath);
                    ct.ThrowIfCancellationRequested();

                    processor = new MultiModalProcessor(model);
                    ct.ThrowIfCancellationRequested();

                    tokenizerStream = processor.CreateStream();
                    ct.ThrowIfCancellationRequested();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    Dispose();
                }
            },
            ct);
    }

    private void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();

        tokenizerStream?.Dispose();
        processor?.Dispose();
        model?.Dispose();
    }

    private static async Task<StorageFile> PickFileAsync()
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
        return file;
    }

    private async IAsyncEnumerable<string> InferStreaming(string question, string imagePath, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (model == null || processor == null || tokenizerStream == null)
        {
            yield break;
        }

        var images = Images.Load([imagePath]);

        var prompt = $@"<|user|>\n<|image_1|>\n{question}<|end|>\n<|assistant|>\n";
        string[] stopTokens = ["</s>", "<|user|>", "<|end|>", "<|assistant|>"];

        var inputTensors = processor.ProcessImages(prompt, images);

        using GeneratorParams generatorParams = new(model);
        generatorParams.SetSearchOption("max_length", 4096);

        ct.ThrowIfCancellationRequested();

        using var generator = new Generator(model, generatorParams);
        generator.SetInputs(inputTensors);

        while (!generator.IsDone())
        {
            ct.ThrowIfCancellationRequested();

            await Task.Delay(0, ct).ConfigureAwait(false);

            // This step takes a long time, theoretically, most cancellation get hung here
            generator.GenerateNextToken();
            ct.ThrowIfCancellationRequested();

            var part = tokenizerStream.Decode(generator.GetSequence(0)[^1]);

            if (stopTokens.Contains(part))
            {
                break;
            }

            ct.ThrowIfCancellationRequested();
            yield return part;
        }
    }

    private async void LoadButton_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickFileAsync();
        if (file != null)
        {
            imageFile = file;
            LoadImage(imageFile);

            await DescribeTheImage();
        }

        LoadImageButton.Focus(FocusState.Programmatic);
    }

    private async void Button_Click(object sender, RoutedEventArgs e)
    {
        if (ButtonTextBlock.Text == "Run sample" && imageFile != null)
        {
            await DescribeTheImage();
        }
        else if (ButtonTextBlock.Text == "Cancel" && _cts != null)
        {
            CancelGeneration();
        }
    }

    private void CancelGeneration()
    {
        _cts!.Cancel();
        DescribeImageButton.IsEnabled = false;
        ButtonTextBlock.Text = "Canceling, this could take a while";
    }

    private async void LoadImage(StorageFile file)
    {
        using IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.Read);
        BitmapImage bitmapImage = new();
        await bitmapImage.SetSourceAsync(fileStream);
        DefaultImage.Source = bitmapImage;
        imageFile = file;
    }

    private async Task DescribeTheImage()
    {
        _cts = new CancellationTokenSource();
        try
        {
            _cts.Token.ThrowIfCancellationRequested();

            LoadImageButton.IsEnabled = false;
            ButtonTextBlock.Text = "Cancel";
            Output.Text = string.Empty;
            Loader.IsActive = true;
            Loader.Visibility = Visibility.Visible;

            _cts.Token.ThrowIfCancellationRequested();

            await Task.Run(async () =>
            {
                try
                {
                    await foreach (var part in InferStreaming("What is this image", imageFile!.Path, _cts.Token))
                    {
                        DispatcherQueue.TryEnqueue(() => Output.Text += part);
                    }
                }
                catch
                {
                }
                finally
                {
                    ResetState();
                }
            });
        }
        catch
        {
            ResetState();
        }
    }

    private void ResetState()
    {
        _cts = null;
        DispatcherQueue.TryEnqueue(() =>
        {
            ButtonTextBlock.Text = "Run sample";
            DescribeImageButton.IsEnabled = true;
            LoadImageButton.IsEnabled = true;
            Loader.IsActive = false;
            Loader.Visibility = Visibility.Collapsed;
        });
    }
}
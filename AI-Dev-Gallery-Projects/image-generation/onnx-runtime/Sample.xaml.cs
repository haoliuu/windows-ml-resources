using AIDevGallery.Sample.Utils;
using Microsoft.ML.OnnxRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace AIDevGallery.Sample;

/// <summary>
/// An empty page that can be used on its own or navigated to within a Frame.
/// </summary>

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private string prompt = string.Empty;
    private bool modelReady;
    private CancellationTokenSource cts = new();
    private StableDiffusion? stableDiffusion;
    private bool isCanceling;
    private Task? inferenceTask;

    private bool isImeActive = true;

    public Sample()
    {
        this.Unloaded += (s, e) => CleanUp();
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // TODO: Set this to the path of your local ONNX model directory.
        // Example: @"C:\Models\stable-diffusion-v1-4\onnx"
        string parentFolder = @"<YOUR_STABLE_DIFFUSION_MODEL_PATH>";

        // Execution Provider (EP) configuration.
        // Change epName and deviceType to use different EP.
        // Set policy to ExecutionProviderDevicePolicy for automatic EP selection.
        ExecutionProviderDevicePolicy? policy = null;
        string? epName = "CPU";
        bool compileModel = false;
        string? deviceType = "CPU";

        try
        {
            stableDiffusion = new StableDiffusion(parentFolder);
            await stableDiffusion.InitializeAsync(policy, epName, compileModel, deviceType);
        }
        catch (Exception ex)
        {
            App.Window?.ShowException(ex);
        }

        modelReady = true;

        App.Window?.ModelLoaded();
    }

    private void CleanUp()
    {
        cts?.Cancel();
        cts?.Dispose();
        stableDiffusion?.Dispose();
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        await DoStableDiffusion();
    }

    private async void TextBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && sender is TextBox && InputBox.Text.Length > 0 && isImeActive == false)
        {
            await DoStableDiffusion();
        }

        isImeActive = true;
    }

    private void TextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        isImeActive = false;
    }

    private async Task DoStableDiffusion()
    {
        if (!modelReady || isCanceling)
        {
            return;
        }

        if (inferenceTask != null)
        {
            cts.Cancel();
            isCanceling = true;
            GenerateButton.Content = "Canceling...";
            await inferenceTask;
            isCanceling = false;
            return;
        }

        SaveButton.IsEnabled = false;
        GenerateButton.Content = "Stop";

        prompt = InputBox.Text;

        Loader.IsActive = true;
        Loader.Visibility = Visibility.Visible;
        DefaultImage.Visibility = Visibility.Collapsed;
        InputBox.IsEnabled = false;

        CancellationToken token = CancelGenerationAndGetNewToken();

        inferenceTask = Task.Run(
            () =>
            {
                try
                {
                    if (stableDiffusion!.Inference(prompt, token) is Bitmap image)
                    {
                        this.DispatcherQueue.TryEnqueue(() =>
                        {
                            BitmapImage bitmapImage = BitmapFunctions.ConvertBitmapToBitmapImage(image);
                            DefaultImage.Source = bitmapImage;
                            SaveButton.IsEnabled = true;
                            DefaultImage.Visibility = Visibility.Visible;
                        });
                    }
                    else
                    {
                        throw new ArgumentException("The inference did not return a valid image.");
                    }
                }
                catch (Exception ex)
                {
                    if (ex is not OperationCanceledException)
                    {
                        this.DispatcherQueue.TryEnqueue(async () =>
                        {
                            ErrorDialog.CloseButtonText = "OK";
                            ErrorDialog.Title = "Error";
                            TextBlock errorTextBlock = new TextBlock()
                            {
                                Text = ex.Message,
                                IsTextSelectionEnabled = true,
                                TextWrapping = TextWrapping.WrapWholeWords
                            };
                            ErrorDialog.Content = errorTextBlock;
                            await ErrorDialog.ShowAsync();
                        });
                    }
                }

                this.DispatcherQueue.TryEnqueue(() => GenerateButton.Content = "Generate");
            },
            token);

        await inferenceTask;
        inferenceTask = null;

        Loader.IsActive = false;
        Loader.Visibility = Visibility.Collapsed;
        InputBox.IsEnabled = true;
    }

    private void CloseButton_Click(ContentDialog sender, ContentDialogButtonClickEventArgs e)
    {
        sender.Hide();
        DefaultImage.Visibility = Visibility.Visible;
        GenerateButton.Content = "Generate";
        InputBox.IsEnabled = true;
    }

    private CancellationToken CancelGenerationAndGetNewToken()
    {
        cts.Cancel();
        cts.Dispose();
        cts = new CancellationTokenSource();
        return cts.Token;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(new Window());
        FileSavePicker picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary
        };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        picker.SuggestedFileName = "image.png";
        picker.FileTypeChoices.Add("PNG", new List<string> { ".png" });

        StorageFile file = await picker.PickSaveFileAsync();

        if (file != null && DefaultImage.Source != null)
        {
            RenderTargetBitmap renderTargetBitmap = new RenderTargetBitmap();
            await renderTargetBitmap.RenderAsync(DefaultImage);

            var pixelBuffer = await renderTargetBitmap.GetPixelsAsync();
            byte[] pixels = pixelBuffer.ToArray();

            using IRandomAccessStream fileStream = await file.OpenAsync(FileAccessMode.ReadWrite);
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.BmpEncoderId, fileStream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)renderTargetBitmap.PixelWidth, (uint)renderTargetBitmap.PixelHeight, 96, 96, pixels);
            await encoder.FlushAsync();
        }
    }
}
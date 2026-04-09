using AIDevGallery.Sample.Utils;
using Microsoft.Extensions.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private const int _maxTokenLength = 1024;
    private IChatClient? chatClient;
    private CancellationTokenSource? cts;
    private bool isProgressVisible;
    private bool isImeActive = true;

    // Model alias and variant ID from the Foundry Local catalog.
    // Run `foundry model list` in a terminal to see available models on your system.
    // The SDK handles model downloading and caching automatically.
    private static readonly string ModelAlias = "qwen2.5-coder-7b";
    private static readonly string VariantId = "qwen2.5-coder-7b-instruct-generic-cpu:4";

    public Sample()
    {
        this.Unloaded += (s, e) => CleanUp();
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        try
        {
            chatClient = await FoundryLocalChatClientFactory.CreateAsync(ModelAlias, VariantId);
            InputTextBox.MaxLength = _maxTokenLength;
        }
        catch (System.Exception ex)
        {
            App.Window?.ShowException(ex);
        }

        App.Window?.ModelLoaded();
    }

    private void CleanUp()
    {
        CancelGeneration();
        chatClient?.Dispose();
    }

    public bool IsProgressVisible
    {
        get => isProgressVisible;
        set
        {
            isProgressVisible = value;
            DispatcherQueue.TryEnqueue(() =>
            {
                OutputProgressBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                StopIcon.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
            });
        }
    }

    public void GenerateText(string topic)
    {
        if (chatClient == null)
        {
            return;
        }

        GenerateTextBlock.Text = string.Empty;
        GenerateButton.Visibility = Visibility.Collapsed;
        StopBtn.Visibility = Visibility.Visible;
        IsProgressVisible = true;
        InputTextBox.IsEnabled = false;

        Task.Run(
            async () =>
            {
                string systemPrompt = "You generate text based on a user-provided topic. Respond with only the generated content and no extraneous text.";
                string userPrompt = "Generate text based on the topic: " + topic;

                cts = new CancellationTokenSource();

                IsProgressVisible = true;

                try
                {
                    await foreach (var messagePart in chatClient.GetStreamingResponseAsync(
                        [
                            new ChatMessage(ChatRole.System, systemPrompt),
                            new ChatMessage(ChatRole.User, userPrompt)
                        ],
                        null,
                        cts.Token))
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            if (isProgressVisible)
                            {
                                StopBtn.Visibility = Visibility.Visible;
                                IsProgressVisible = false;
                            }

                            GenerateTextBlock.Text += messagePart;

                        });
                    }

                }
                catch (Exception ex)
                {
                    if (cts != null && !cts.Token.IsCancellationRequested)
                    {
                        App.Window?.ShowException(ex);
                    }
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    StopBtn.Visibility = Visibility.Collapsed;
                    GenerateButton.Visibility = Visibility.Visible;
                    InputTextBox.IsEnabled = true;
                });

                cts?.Dispose();
                cts = null;
            });
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.InputTextBox.Text.Length > 0)
        {
            GenerateText(InputTextBox.Text);
        }
    }

    private void TextBox_KeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && sender is TextBox && isImeActive == false)
        {
            if (InputTextBox.Text.Length > 0)
            {
                GenerateText(InputTextBox.Text);
            }
        }
        else
        {
            isImeActive = true;
        }
    }

    private void TextBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        isImeActive = false;
    }

    private void CancelGeneration()
    {
        StopBtn.Visibility = Visibility.Collapsed;
        IsProgressVisible = false;
        GenerateButton.Visibility = Visibility.Visible;
        InputTextBox.IsEnabled = true;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        CancelGeneration();
    }

    private void InputBox_Changed(object sender, TextChangedEventArgs e)
    {
        var inputLength = InputTextBox.Text.Length;
        if (inputLength > 0)
        {
            if (inputLength > _maxTokenLength)
            {
                InputTextBox.Description = $"{inputLength} of {_maxTokenLength}. Max characters reached.";
            }
            else
            {
                InputTextBox.Description = $"{inputLength} of {_maxTokenLength}";
            }

            GenerateButton.IsEnabled = inputLength <= _maxTokenLength;
        }
        else
        {
            InputTextBox.Description = string.Empty;
            GenerateButton.IsEnabled = false;
        }
    }
}
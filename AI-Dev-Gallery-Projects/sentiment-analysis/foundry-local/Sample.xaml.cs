using AIDevGallery.Sample.Utils;
using Microsoft.Extensions.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private const int _maxTokenLength = 1024;
    private IChatClient? chatClient;
    private CancellationTokenSource? cts;
    private bool isProgressVisible;

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
        catch (Exception ex)
        {
            App.Window?.ShowException(ex);
        }

        App.Window?.ModelLoaded();
    }

    private void CleanUp()
    {
        CancelSentiment();
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

    public void AnalyzeSentiment(string text)
    {
        if (chatClient == null)
        {
            return;
        }

        SentimentTextBlock.Text = string.Empty;
        SentimentButton.Visibility = Visibility.Collapsed;

        Task.Run(
            async () =>
            {
                var systemPrompt = "You analyze the sentiment of user provided text. " +
                    "Respond in JSON with the following fields:" +
                    "1. Sentiment: An integer between -2 and 2 that maps to the sentiment string values." +
                    "2. SentimentString: which can have a value of either Negative, Slightly Negative, Neutral, Slightly Positive, or Positive." +
                    "Do not reply with anything besides the JSON itself.";

                var userPrompt = "Analyze the sentiment of the following text: " + text;

                cts = new CancellationTokenSource();

                var response = string.Empty;

                var matchFound = false;

                await foreach (var messagePart in chatClient.GetStreamingResponseAsync(
                    [
                        new ChatMessage(ChatRole.System, systemPrompt),
                        new ChatMessage(ChatRole.User, userPrompt)
                    ],
                    new() { MaxOutputTokens = _maxTokenLength },
                    cts.Token))
                {
                    response += messagePart;
                    Match match = SentimentRegex().Match(response);
                    if (match.Success)
                    {
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            StopBtn.Visibility = Visibility.Visible;
                            IsProgressVisible = false;
                            SentimentTextBlock.Text = $"Sentiment: {match.Groups[1].Value}\nSentiment String: {match.Groups[2].Value}";
                            matchFound = true;
                            cts.Cancel();
                        });
                        break;
                    }
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    StopBtn.Visibility = Visibility.Collapsed;
                    SentimentButton.Visibility = Visibility.Visible;
                    if (!matchFound)
                    {
                        SentimentTextBlock.Text = "No sentiment found";
                    }
                });


                cts?.Dispose();
                cts = null;
            });
    }

    private void SentimentButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.InputTextBox.Text.Length > 0)
        {
            StopBtn.Visibility = Visibility.Visible;
            IsProgressVisible = true;
            AnalyzeSentiment(InputTextBox.Text);
        }
    }

    private void CancelSentiment()
    {
        StopBtn.Visibility = Visibility.Collapsed;
        IsProgressVisible = false;
        SentimentButton.Visibility = Visibility.Visible;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        CancelSentiment();
    }

    private void InputBox_Changed(object sender, TextChangedEventArgs e)
    {
        var inputLength = InputTextBox.Text.Length;
        if (inputLength > 0)
        {
            if (inputLength >= _maxTokenLength)
            {
                InputTextBox.Description = $"{inputLength} of {_maxTokenLength}. Max characters reached.";
            }
            else
            {
                InputTextBox.Description = $"{inputLength} of {_maxTokenLength}";
            }

            SentimentButton.IsEnabled = inputLength <= _maxTokenLength;
        }
        else
        {
            InputTextBox.Description = string.Empty;
            SentimentButton.IsEnabled = false;
        }
    }

    [GeneratedRegex("{\\s*\"Sentiment\": (.*),\\s*\"SentimentString\": \"(.*)\"\\s*}", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SentimentRegex();
}
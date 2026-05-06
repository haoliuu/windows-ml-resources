using AIDevGallery.Sample.Utils;
using Microsoft.Extensions.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private IChatClient? model;
    private CancellationTokenSource? cts;
    private bool isProgressVisible;
    private int maxTextLength;

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
            model = await FoundryLocalChatClientFactory.CreateAsync(ModelAlias, VariantId);

            // Increase the default max length to allow larger pieces of code
            // More than 7K will crash
            maxTextLength = 4096;
            InputTextBox.MaxLength = maxTextLength;
        }
        catch (Exception ex)
        {
            App.Window?.ShowException(ex);
        }

        App.Window?.ModelLoaded();
    }

    private void CleanUp()
    {
        CancelExplain();
        model?.Dispose();
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

    public void Explain(string code)
    {
        if (model == null)
        {
            return;
        }

        ExplanationTextBlock.Text = string.Empty;
        ExplainButton.Visibility = Visibility.Collapsed;

        Task.Run(
            async () =>
            {
                string systemPrompt = "You explain user provided code. Provide an explanation of code and no extraneous text. If you can't find code in the user prompt, reply with \"No Code Found.\"";
                string userPrompt = "Explain this code: " + code;

                cts = new CancellationTokenSource();

                var isProgressVisible = true;

                try
                {
                    await foreach (var messagePart in model.GetStreamingResponseAsync(
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
                                IsProgressVisible = false;
                            }

                            ExplanationTextBlock.Text += messagePart;

                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Code explanation failed: {ex.Message}");
                    CancelExplain();
                }

                cts?.Dispose();
                cts = null;

                DispatcherQueue.TryEnqueue(() =>
                {
                    StopBtn.Visibility = Visibility.Collapsed;
                    ExplainButton.IsEnabled = true;
                    ExplainButton.Visibility = Visibility.Visible;
                });
            });
    }

    private void ExplainButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.InputTextBox.Text.Length > 0)
        {
            StopBtn.Visibility = Visibility.Visible;
            ExplainButton.Visibility = Visibility.Collapsed;
            ExplainButton.IsEnabled = false;
            IsProgressVisible = true;
            Explain(InputTextBox.Text);
        }
    }

    private void CancelExplain()
    {
        IsProgressVisible = false;
        StopBtn.Visibility = Visibility.Collapsed;
        ExplainButton.Visibility = Visibility.Visible;
        ExplainButton.IsEnabled = true;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        CancelExplain();
    }

    private void InputBox_Changed(object sender, TextChangedEventArgs e)
    {
        var inputLength = InputTextBox.Text.Length;
        if (inputLength > 0)
        {
            if (inputLength >= maxTextLength)
            {
                InputTextBox.Description = $"{inputLength} of {maxTextLength}. Max characters reached.";
            }
            else
            {
                InputTextBox.Description = $"{inputLength} of {maxTextLength}";
            }

            ExplainButton.IsEnabled = inputLength <= maxTextLength;
        }
        else
        {
            InputTextBox.Description = string.Empty;
            ExplainButton.IsEnabled = false;
        }
    }
}
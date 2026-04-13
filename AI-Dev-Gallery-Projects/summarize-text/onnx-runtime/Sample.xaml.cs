using AIDevGallery.Sample.Utils;
using Microsoft.Extensions.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Threading;
using System.Threading.Tasks;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private const int _defaultMaxLength = 1024;
    private IChatClient? chatClient;
    private CancellationTokenSource? cts;
    private bool isProgressVisible;

    // TODO: Set this to the path of your local ONNX model directory.
    // Example: @"C:\Models\Phi-4-mini-instruct-onnx\cpu-int4-rtn-block-32-acc-level-4"
    private static readonly string ModelPath = @"<YOUR_ONNX_MODEL_PATH>";

    // Prompt template for the Phi model family.
    // Different models may require different templates — check the model's documentation.
    private static readonly LlmPromptTemplate PromptTemplate = new()
    {
        System = "<|system|>\n{{CONTENT}}<|end|>\n",
        User = "<|user|>\n{{CONTENT}}<|end|>\n",
        Assistant = "<|assistant|>\n{{CONTENT}}<|end|>\n",
        Stop = ["<|system|>", "<|user|>", "<|assistant|>", "<|end|>"]
    };

    public Sample()
    {
        this.Unloaded += (s, e) => CleanUp();
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        try
        {
            chatClient = await OnnxRuntimeGenAIChatClientFactory.CreateAsync(ModelPath, PromptTemplate);
            InputTextBox.MaxLength = _defaultMaxLength;
        }
        catch (System.Exception ex)
        {
            App.Window?.ShowException(ex);
        }

        App.Window?.ModelLoaded();
    }

    private void Page_Loaded()
    {
        InputTextBox.Focus(FocusState.Programmatic);
    }

    private void CleanUp()
    {
        CancelSummary();
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

    public void SummarizeText(string text)
    {
        if (chatClient == null)
        {
            return;
        }

        SummaryTextBlock.Text = string.Empty;
        SummarizeButton.Visibility = Visibility.Collapsed;

        Task.Run(
            async () =>
            {
                string systemPrompt = "You summarize user-provided text. " +
                "Respond with only the summary itself and no extraneous text.";
                string userPrompt = "Summarize this text: " + text;

                cts = new CancellationTokenSource();

                IsProgressVisible = true;
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
                        if (IsProgressVisible)
                        {
                            StopBtn.Visibility = Visibility.Visible;
                            IsProgressVisible = false;
                        }

                        SummaryTextBlock.Text += messagePart;

                    });
                }

                DispatcherQueue.TryEnqueue(() =>
                {
                    StopBtn.Visibility = Visibility.Collapsed;
                    SummarizeButton.Visibility = Visibility.Visible;
                });

                cts?.Dispose();
                cts = null;
            });
    }

    private void SummarizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (this.InputTextBox.Text.Length > 0)
        {
            IsProgressVisible = true;
            StopBtn.Visibility = Visibility.Visible;
            SummarizeText(InputTextBox.Text);
        }
    }

    private void CancelSummary()
    {
        IsProgressVisible = false;
        StopBtn.Visibility = Visibility.Collapsed;
        cts?.Cancel();
        cts?.Dispose();
        cts = null;
    }

    private void StopBtn_Click(object sender, RoutedEventArgs e)
    {
        CancelSummary();
    }

    private void InputBox_Changed(object sender, TextChangedEventArgs e)
    {
        var inputLength = InputTextBox.Text.Length;
        if (inputLength > 0)
        {
            if (inputLength >= _defaultMaxLength)
            {
                InputTextBox.Description = $"{inputLength} of {_defaultMaxLength}. Max characters reached.";
            }
            else
            {
                InputTextBox.Description = $"{inputLength} of {_defaultMaxLength}";
            }

            SummarizeButton.IsEnabled = inputLength <= _defaultMaxLength;
        }
        else
        {
            InputTextBox.Description = string.Empty;
            SummarizeButton.IsEnabled = false;
        }
    }
}
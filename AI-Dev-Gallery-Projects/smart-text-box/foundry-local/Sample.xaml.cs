using AIDevGallery.Sample.Utils;
using Microsoft.Extensions.AI;
using System;
using System.Threading.Tasks;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private IChatClient? _model;

    // Model alias and variant ID from the Foundry Local catalog.
    // Run `foundry model list` in a terminal to see available models on your system.
    // The SDK handles model downloading and caching automatically.
    private static readonly string ModelAlias = "qwen2.5-coder-7b";
    private static readonly string VariantId = "qwen2.5-coder-7b-instruct-generic-cpu:4";

    public Sample()
    {
        this.Unloaded += (s, e) => CleanUp();
        try
        {
            this.InitializeComponent();
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine(e.Message);
        }
    }

    protected override async void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        try
        {
            _model = await FoundryLocalChatClientFactory.CreateAsync(ModelAlias, VariantId);
        }
        catch (Exception ex)
        {
            App.Window?.ShowException(ex);
        }

        if (_model != null)
        {
            this.SmartTextBox.ChatClient = _model;
        }

        App.Window?.ModelLoaded();
    }

    private void CleanUp()
    {
        _model?.Dispose();
    }
}
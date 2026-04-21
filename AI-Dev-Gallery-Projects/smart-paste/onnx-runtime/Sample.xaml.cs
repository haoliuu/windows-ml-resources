using AIDevGallery.Sample.Utils;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIDevGallery.Sample;

internal sealed partial class Sample : Microsoft.UI.Xaml.Controls.Page
{
    private IChatClient? model;
    public List<string> FieldLabels { get; set; } = ["Name", "Address", "City", "State", "Zip"];

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
            model = await OnnxRuntimeGenAIChatClientFactory.CreateAsync(ModelPath, PromptTemplate);
        }
        catch (Exception ex)
        {
            App.Window?.ShowException(ex);
        }

        if (model != null)
        {
            this.SmartForm.Model = model;
        }

        App.Window?.ModelLoaded();
    }

    private void CleanUp()
    {
        model?.Dispose();
    }
}
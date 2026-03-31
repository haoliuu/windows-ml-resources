using Microsoft.Extensions.AI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AIDevGallery.Sample.Utils;

internal partial class ChatTemplateSelector : DataTemplateSelector
{
    public DataTemplate UserTemplate { get; set; } = null!;

    public DataTemplate AssistantTemplate { get; set; } = null!;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
    {
        Message? selectedObject = item as Message;

        if (selectedObject?.Role == ChatRole.User)
        {
            return UserTemplate;
        }
        else
        {
            return AssistantTemplate;
        }
    }
}
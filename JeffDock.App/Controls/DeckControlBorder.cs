using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace JeffDock.App.Controls;

internal sealed class DeckControlBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new DeckControlPeer(this);

    private sealed class DeckControlPeer(DeckControlBorder owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => "DeckControl";
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
        protected override bool IsControlElementCore() => true;
        protected override bool IsContentElementCore() => true;
    }
}

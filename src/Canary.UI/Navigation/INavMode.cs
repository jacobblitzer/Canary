namespace Canary.UI.Navigation;

// Phase 7 / design §C4 — abstraction for the new top-level nav tabs.
// Each implementation owns one tab's content (created lazily on first
// activation). The MainForm hosts the active mode's Control inside a
// TabControl tab page.
//
// Pragmatic Phase 7 scope: tab strip lives at the top of the body
// (above the existing SplitContainer) rather than nested below the
// TreeView per the design doc's exact ASCII. The INavMode contract
// is the same shape; future polish phases can rearrange placement
// without changing this interface.
public interface INavMode
{
    string Name { get; }
    string Description { get; }

    // Returns the Control that should fill the active tab page. Called
    // lazily on first activation; the returned control may be cached by
    // the implementation for re-use across tab switches.
    Control CreateContent();
}

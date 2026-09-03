using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace BanglaHost.App.Services
{
    /// <summary>
    /// Runtime UI localizer. When the user picks Bangla (Config.Language == "bn-BD"),
    /// every page is translated as it loads by walking its visual tree and swapping any
    /// text that EXACTLY matches an English key in the dictionary (see LocalizerMap.cs).
    ///
    /// Exact full-string match only — never a substring — so it never touches domain
    /// names, versions, file paths, tokens, or anything the user typed. Idempotent (the
    /// Bangla output is never itself a key) and per-node exception-safe.
    ///
    /// The sidebar is localized separately by MRT (x:Uid + Strings\bn-BD\Resources.resw);
    /// this covers the ~50 content pages, which are plain-English XAML. English keys are
    /// the CORRECTED source strings (real — … 🙏), matching the live control text.
    /// </summary>
    public static partial class Localizer
    {
        // The language decision is cached for the session: it only changes via Settings,
        // which restarts the app. Avoids hitting disk (Config.Load) on every visited node.
        private static bool? _active;

        /// <summary>True when the UI should be rendered in Bangla.</summary>
        public static bool IsActive => _active ??= ResolveActive();

        private static bool ResolveActive()
        {
            try
            {
                var lang = BanglaHost.Core.Config.Load().Language;
                return string.Equals(lang, "bn-BD", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>Translate a single string for code-behind / dynamically-built text.
        /// Returns the input unchanged when not in Bangla mode or when there is no
        /// exact-match entry. Safe to wrap around any label.</summary>
        public static string T(string? s)
        {
            if (!IsActive || string.IsNullOrEmpty(s)) return s ?? "";
            return Swap(s);
        }

        /// <summary>Walk a visual tree and translate all discovered UI text in place.
        /// No-op when not in Bangla mode. Safe to call repeatedly (idempotent) and safe
        /// against layout/teardown races (every access is guarded).</summary>
        public static void Localize(DependencyObject? root)
        {
            if (!IsActive || root is null) return;
            try { Walk(root); } catch { }
        }

        /// <summary>Translate a ContentDialog's title, button captions and (string) content.
        /// Called centrally from DialogQueue so most dialogs are covered without per-call edits.
        /// Panel content is handled by the visual walker once the dialog is in the live tree.</summary>
        public static void LocalizeDialog(ContentDialog? dlg)
        {
            if (!IsActive || dlg is null) return;
            try
            {
                if (dlg.Title is string t)              dlg.Title = Swap(t);
                if (!string.IsNullOrEmpty(dlg.PrimaryButtonText))   dlg.PrimaryButtonText   = Swap(dlg.PrimaryButtonText);
                if (!string.IsNullOrEmpty(dlg.SecondaryButtonText)) dlg.SecondaryButtonText = Swap(dlg.SecondaryButtonText);
                if (!string.IsNullOrEmpty(dlg.CloseButtonText))     dlg.CloseButtonText     = Swap(dlg.CloseButtonText);
                if (dlg.Content is string c)            dlg.Content = Swap(c);
            }
            catch { }
        }

        // ── visual-tree walk ────────────────────────────────────────────────────

        private static void Walk(DependencyObject node)
        {
            try { Apply(node); } catch { }

            int n;
            try { n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node); }
            catch { return; }

            for (int i = 0; i < n; i++)
            {
                DependencyObject? child = null;
                try { child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node, i); }
                catch { }
                if (child != null) Walk(child);
            }
        }

        private static void Apply(DependencyObject node)
        {
            switch (node)
            {
                case TextBlock tb:
                    TranslateInlines(tb.Inlines);
                    break;

                case TextBox tbx:
                    tbx.PlaceholderText = Swap(tbx.PlaceholderText);
                    if (tbx.Header is string th) tbx.Header = Swap(th);
                    break;

                case AutoSuggestBox asb:
                    asb.PlaceholderText = Swap(asb.PlaceholderText);
                    if (asb.Header is string ah) asb.Header = Swap(ah);
                    break;

                case PasswordBox pb:
                    pb.PlaceholderText = Swap(pb.PlaceholderText);
                    if (pb.Header is string ph) pb.Header = Swap(ph);
                    break;

                case NumberBox nb:
                    nb.PlaceholderText = Swap(nb.PlaceholderText);
                    if (nb.Header is string nh) nb.Header = Swap(nh);
                    break;

                case ComboBox cb:
                    cb.PlaceholderText = Swap(cb.PlaceholderText);
                    if (cb.Header is string ch) cb.Header = Swap(ch);
                    break;

                case ToggleSwitch ts:
                    ts.OnContent  = SwapObj(ts.OnContent);
                    ts.OffContent = SwapObj(ts.OffContent);
                    if (ts.Header is string tsh) ts.Header = Swap(tsh);
                    break;

                case InfoBar ib:
                    ib.Title   = Swap(ib.Title);
                    ib.Message = Swap(ib.Message);
                    break;

                // Expander keeps its label in Header (a ContentControl, so it must precede the
                // generic ContentControl case below). Body content is reached by recursion.
                case Expander ex:
                    if (ex.Header is string exh) ex.Header = Swap(exh);
                    if (ex.Content is string exc) ex.Content = Swap(exc);
                    break;

                case PivotItem pvi:
                    if (pvi.Header is string pvh) pvi.Header = Swap(pvh);
                    break;

                // AppBar commands keep their caption in Label (not Content). AppBarButton derives
                // from Button, so it MUST precede the Button case below; AppBarToggleButton derives
                // from ToggleButton. Both may also host a MenuFlyout.
                case AppBarButton abb:
                    if (abb.Label is string abl) abb.Label = Swap(abl);
                    if (abb.Flyout is MenuFlyout abf) TranslateMenuFlyout(abf);
                    break;

                case AppBarToggleButton abt:
                    if (abt.Label is string abtl) abt.Label = Swap(abtl);
                    break;

                // A CommandBar's PrimaryCommands render in the tree (the walker reaches them via the
                // AppBarButton case), but SecondaryCommands live in the overflow popup and are NOT
                // reachable by VisualTreeHelper — translate both here. Idempotent, so any overlap is fine.
                case CommandBar cbar:
                    foreach (var c in cbar.PrimaryCommands)   TranslateCommandBarElement(c);
                    foreach (var c in cbar.SecondaryCommands) TranslateCommandBarElement(c);
                    break;

                // Button (and DropDownButton/SplitButton, which derive from it) may host an
                // inline MenuFlyout — translate its items too. Must precede ContentControl.
                case Button btn:
                    if (btn.Content is string bcs) btn.Content = Swap(bcs);
                    if (btn.Flyout is MenuFlyout bmf) TranslateMenuFlyout(bmf);
                    break;

                // Button, ToggleButton, HyperlinkButton, NavigationViewItem, ComboBoxItem,
                // ContentDialog … are all ContentControls: translate a string Content only.
                case ContentControl cc:
                    if (cc.Content is string cs) cc.Content = Swap(cs);
                    break;
            }

            // Right-click context menu on any element.
            if (node is FrameworkElement fe && fe.ContextFlyout is MenuFlyout cmf)
                TranslateMenuFlyout(cmf);

            // Attached hover tooltip (ToolTipService.ToolTip) when it's a plain string.
            try
            {
                if (ToolTipService.GetToolTip(node) is string tip)
                {
                    var swapped = Swap(tip);
                    if (!ReferenceEquals(swapped, tip)) ToolTipService.SetToolTip(node, swapped);
                }
            }
            catch { }
        }

        private static void TranslateInlines(InlineCollection? inlines)
        {
            if (inlines is null) return;
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Run run:      run.Text = Swap(run.Text); break;
                    case Hyperlink hl: TranslateInlines(hl.Inlines); break;
                    case Span span:    TranslateInlines(span.Inlines); break;   // Bold/Italic/Underline derive from Span
                }
            }
        }

        // MenuFlyout items live in a separate popup tree (not reached by VisualTreeHelper), but when
        // the flyout is declared inline in XAML the MenuFlyout object and its Items exist as soon as
        // the host button does — so we can translate the captions eagerly at walk time.
        private static void TranslateMenuFlyout(MenuFlyout mf)
        {
            var items = mf.Items;
            if (items is null) return;
            foreach (var item in items) TranslateMenuItem(item);
        }

        private static void TranslateMenuItem(MenuFlyoutItemBase? item)
        {
            switch (item)
            {
                case MenuFlyoutSubItem sub:                 // has children; also carries its own Text
                    sub.Text = Swap(sub.Text);
                    foreach (var child in sub.Items) TranslateMenuItem(child);
                    break;
                case ToggleMenuFlyoutItem tmi:              // derives from MenuFlyoutItem — check first
                    tmi.Text = Swap(tmi.Text);
                    break;
                case MenuFlyoutItem mi:
                    mi.Text = Swap(mi.Text);
                    break;
                // MenuFlyoutSeparator: nothing to translate
            }
        }

        // A single item in a CommandBar's Primary/Secondary command list.
        private static void TranslateCommandBarElement(ICommandBarElement? el)
        {
            switch (el)
            {
                case AppBarButton b:
                    if (b.Label is string bl) b.Label = Swap(bl);
                    if (b.Flyout is MenuFlyout mf) TranslateMenuFlyout(mf);
                    break;
                case AppBarToggleButton t:
                    if (t.Label is string tl) t.Label = Swap(tl);
                    break;
                // AppBarSeparator: nothing to translate
            }
        }

        private static object SwapObj(object o) => o is string s ? Swap(s) : o;

        /// <summary>Exact-match lookup, whitespace-aware. XAML often wraps literal text with
        /// leading/trailing newlines+indentation; we match on the trimmed core and re-attach the
        /// original surrounding whitespace so layout is preserved.</summary>
        private static string Swap(string? s)
        {
            if (string.IsNullOrEmpty(s)) return s ?? "";

            if (Map.TryGetValue(s, out var direct)) return direct;

            var trimmed = s.Trim();
            if (trimmed.Length != s.Length && trimmed.Length > 0 && Map.TryGetValue(trimmed, out var core))
            {
                int lead = 0;
                while (lead < s.Length && char.IsWhiteSpace(s[lead])) lead++;
                int trail = 0;
                while (trail < s.Length - lead && char.IsWhiteSpace(s[s.Length - 1 - trail])) trail++;
                return string.Concat(s.AsSpan(0, lead), core, s.AsSpan(s.Length - trail, trail));
            }
            return s;
        }
    }
}

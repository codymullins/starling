using Tessera.Css.Parser;

namespace Tessera.Css.UserAgent;

public static class UaStyleSheet
{
    public const string Source = """
        html, body, div, section, article, main, header, footer, nav, aside,
        p, h1, h2, h3, h4, h5, h6, blockquote, pre, address, figure, figcaption,
        ul, ol, menu, li, dl, dt, dd, form, fieldset, legend, caption {
          display: block;
        }

        /* Table elements. PLACEHOLDER: a real CSS table layout pass (display:
           table / table-row / table-cell with column-width resolution, row
           grouping, border-collapse, etc.) is not yet implemented. To keep the
           common legacy-footer pattern (Google's footer:
           <table><tr><td>...</td><td>...</td></tr></table>) from collapsing
           into a vertical stack, we approximate the visual outcome by having
           cells flow as `inline-block`s inside a block-level row. Cells get a
           minimal 1px padding to mirror the historical UA spacing browsers
           apply (`border-spacing: 2px` plus cell padding). Replace this block
           when a proper table formatting context lands. */
        table { display: block; border-spacing: 0; }
        thead, tbody, tfoot { display: block; }
        tr { display: block; }
        td, th { display: inline-block; padding: 1px; vertical-align: top; }

        head, title, meta, link, style, script {
          display: none;
        }

        body {
          margin: 8px;
          color: black;
          background-color: white;
          font-family: serif;
          font-size: 16px;
        }

        p { margin: 1em 0; }
        h1 { display: block; font-size: 2em; margin: 0.67em 0; font-weight: 700; }
        h2 { display: block; font-size: 1.5em; margin: 0.83em 0; font-weight: 700; }
        h3 { display: block; font-size: 1.17em; margin: 1em 0; font-weight: 700; }
        h4 { display: block; margin: 1.33em 0; font-weight: 700; }
        h5 { display: block; font-size: 0.83em; margin: 1.67em 0; font-weight: 700; }
        h6 { display: block; font-size: 0.67em; margin: 2.33em 0; font-weight: 700; }
        ul, ol, menu { margin: 1em 0; padding-left: 40px; }
        li { display: list-item; }
        hr {
          display: block;
          margin: 0.5em 0;
          border-style: solid;
          border-color: #888888;
          border-width: 1px 0 0 0;
        }
        a { color: blue; text-decoration: underline; }
        b, strong { font-weight: 700; }
        i, em, cite { font-style: italic; }
        pre { white-space: pre; font-family: monospace; }

        /* HTML4 presentational tags. Legacy pages (notably google.com) still
           emit these, so the UA sheet has to render them sensibly even though
           they're deprecated in favour of CSS. We map the structural part to
           CSS here; the per-attribute styling (e.g. `<font color>`) is a
           separate, larger job. */
        center { display: block; text-align: center; }
        font { display: inline; }
        nobr { white-space: nowrap; }
        tt, code, kbd, samp { font-family: monospace; }

        /* Form controls. Real browsers render these as platform-native widgets;
           we approximate with CSS so an unstyled `<input>`/`<button>` shows up
           as a recognisable box. Author CSS still overrides everything here. */
        input, button, select, textarea {
          display: inline-block;
          font-family: sans-serif;
          font-size: 13px;
          color: black;
          background-color: white;
          border: 1px solid #767676;
          padding: 1px 2px;
          margin: 0;
        }
        button, input[type="submit"], input[type="button"], input[type="reset"] {
          background-color: #efefef;
          padding: 2px 7px;
          text-align: center;
        }
        input[type="hidden"] { display: none; }

        /* Without an explicit height an empty `<input>` collapses to 0 because
           we don't render the platform widget's intrinsic chrome. Force a
           text-line's worth of min-height so empty fields are still visible;
           author CSS that sets a real height/min-height wins over this. */
        input { min-height: 1.2em; }

        /* <textarea> is multi-line; we don't have multi-line content layout
           for inline-block yet, so it ends up looking like a wide single-line
           field. Monospace matches platform default and helps users tell
           textareas from inputs visually. */
        textarea {
          padding: 2px;
          font-family: monospace;
        }

        /* <select>: without JS we can't actually open a dropdown. Approximate
           the closed state by showing only the first option as its label. */
        select { padding: 1px 4px; }
        option { display: none; }
        select > option:first-child { display: inline; }

        /* Muted disabled state. We don't track focus/active/checked yet, but
           the :disabled selector matches the HTML attribute today. */
        input:disabled, button:disabled, select:disabled, textarea:disabled {
          color: #777;
          background-color: #efefef;
          border-color: #c0c0c0;
        }
        """;

    public static StyleSheet Parse() => CssParser.ParseStyleSheet(Source, StyleOrigin.UserAgent);
}

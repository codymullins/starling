# Starling website

The static site for Starling, served at GitHub Pages.

It is plain HTML and CSS with no build step. To preview it, open
`index.html` in a browser, or serve the folder:

```bash
python3 -m http.server --directory site 8080
# then open http://localhost:8080
```

## How it ships

The workflow at `.github/workflows/pages.yml` uploads this folder and deploys
it to GitHub Pages on every push to `main` that touches `site/`. You can also
run it by hand from the Actions tab.

One-time setup: in the repo settings, under Pages, set the source to
"GitHub Actions".

## Files

- `index.html` — the whole page.
- `css/styles.css` — the styles.
- `img/` — the logo and screenshot.
- `.nojekyll` — tells Pages to serve the files as-is.

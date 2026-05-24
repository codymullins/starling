// Mirrors the original page's external <script src> element. Kept tiny on
// purpose: it just proves the script element loads and runs in the engine,
// without altering the page's layout.
(function () {
  "use strict";
  document.documentElement.setAttribute("data-js", "ran");
  if (window.console && console.log) {
    console.log("words: script element executed");
  }
})();

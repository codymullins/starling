// Web Animations API demo — drives a box purely from JavaScript via
// element.animate(), with live play / pause / resume / cancel / finish controls
// and a status readout. ES5-style, no build step.

(function () {
  "use strict";

  var box = document.getElementById("waapi-box");
  var status = document.getElementById("waapi-status");
  if (!box || !status) return;

  // A looping, alternating slide + colour shift. Animating both transform and
  // background-color shows two interpolation kinds at once.
  var keyframes = [
    { transform: "translateX(-90px)", backgroundColor: "rgb(108, 123, 255)" },
    { transform: "translateX(90px)",  backgroundColor: "rgb(0, 212, 180)" }
  ];
  var options = {
    duration: 1600,
    iterations: Infinity,
    direction: "alternate",
    easing: "ease-in-out"
  };

  var anim = null;

  function ensure() {
    if (!anim) anim = box.animate(keyframes, options);
    return anim;
  }

  document.getElementById("waapi-play").addEventListener("click", function () {
    ensure().play();
  });
  document.getElementById("waapi-pause").addEventListener("click", function () {
    if (anim) anim.pause();
  });
  document.getElementById("waapi-reverse").addEventListener("click", function () {
    if (anim) anim.play();
  });
  document.getElementById("waapi-cancel").addEventListener("click", function () {
    if (anim) { anim.cancel(); anim = null; }
  });
  document.getElementById("waapi-finish").addEventListener("click", function () {
    if (anim) anim.finish();
  });

  // Mirror the animation's state into the status line every frame.
  function tick() {
    if (anim) {
      var t = Math.round(anim.currentTime || 0);
      status.textContent = anim.playState + " · " + t + " ms";
    } else {
      status.textContent = "idle · 0 ms";
    }
    requestAnimationFrame(tick);
  }
  requestAnimationFrame(tick);

  // Start playing on load so the page is alive immediately.
  ensure();
})();

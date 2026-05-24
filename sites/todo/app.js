// Vanilla-JS todo app — no build step, no dependencies.
// State lives in memory and is mirrored to localStorage when available so the
// list survives reloads. Everything degrades gracefully if storage is missing.

(function () {
  "use strict";

  var STORAGE_KEY = "starling.todo.v1";

  var els = {
    composer: document.getElementById("composer"),
    input: document.getElementById("new-todo"),
    list: document.getElementById("list"),
    empty: document.getElementById("empty"),
    toolbar: document.getElementById("toolbar"),
    count: document.getElementById("count"),
    status: document.getElementById("status"),
    clear: document.getElementById("clear-completed"),
    filters: document.querySelectorAll(".filter")
  };

  var todos = load();
  var filter = "all"; // all | active | completed

  // --- persistence -------------------------------------------------------

  function load() {
    try {
      var raw = window.localStorage.getItem(STORAGE_KEY);
      var parsed = raw ? JSON.parse(raw) : [];
      return Array.isArray(parsed) ? parsed : [];
    } catch (e) {
      return [];
    }
  }

  function save() {
    try {
      window.localStorage.setItem(STORAGE_KEY, JSON.stringify(todos));
    } catch (e) {
      // Private mode / no storage — keep working from memory.
    }
  }

  // --- mutations ---------------------------------------------------------

  function addTodo(title) {
    title = title.trim();
    if (!title) return;
    todos.push({ id: Date.now() + "-" + Math.random().toString(36).slice(2, 7), title: title, done: false });
    save();
    render();
  }

  function toggle(id) {
    for (var i = 0; i < todos.length; i++) {
      if (todos[i].id === id) { todos[i].done = !todos[i].done; break; }
    }
    save();
    render();
  }

  function remove(id) {
    todos = todos.filter(function (t) { return t.id !== id; });
    save();
    render();
  }

  function clearCompleted() {
    todos = todos.filter(function (t) { return !t.done; });
    save();
    render();
  }

  // --- rendering ---------------------------------------------------------

  function visible() {
    if (filter === "active") return todos.filter(function (t) { return !t.done; });
    if (filter === "completed") return todos.filter(function (t) { return t.done; });
    return todos;
  }

  function render() {
    var rows = visible();

    els.list.textContent = "";
    rows.forEach(function (todo) {
      els.list.appendChild(row(todo));
    });

    var hasAny = todos.length > 0;
    els.empty.hidden = rows.length > 0;
    els.toolbar.hidden = !hasAny;

    if (rows.length === 0) {
      els.empty.textContent = hasAny
        ? "No tasks in this view."
        : "Nothing here. Add your first task above.";
    }

    var remaining = todos.filter(function (t) { return !t.done; }).length;
    els.count.textContent = remaining + (remaining === 1 ? " left" : " left");
    els.status.textContent = hasAny
      ? remaining + " of " + todos.length + " to go"
      : "No tasks yet";

    for (var i = 0; i < els.filters.length; i++) {
      var btn = els.filters[i];
      btn.setAttribute("aria-pressed", btn.getAttribute("data-filter") === filter ? "true" : "false");
    }
  }

  function row(todo) {
    var li = document.createElement("li");
    li.className = "item" + (todo.done ? " is-done" : "");
    li.setAttribute("data-id", todo.id);

    var check = document.createElement("input");
    check.type = "checkbox";
    check.className = "item__check";
    check.checked = todo.done;
    check.setAttribute("aria-label", "Mark complete");
    check.addEventListener("change", function () { toggle(todo.id); });

    var title = document.createElement("span");
    title.className = "item__title";
    title.textContent = todo.title;

    var del = document.createElement("button");
    del.type = "button";
    del.className = "item__delete";
    del.setAttribute("aria-label", "Delete task");
    del.textContent = "×";
    del.addEventListener("click", function () { remove(todo.id); });

    li.appendChild(check);
    li.appendChild(title);
    li.appendChild(del);
    return li;
  }

  // --- wiring ------------------------------------------------------------

  els.composer.addEventListener("submit", function (e) {
    e.preventDefault();
    addTodo(els.input.value);
    els.input.value = "";
    els.input.focus();
  });

  els.clear.addEventListener("click", clearCompleted);

  for (var i = 0; i < els.filters.length; i++) {
    els.filters[i].addEventListener("click", function (e) {
      filter = e.currentTarget.getAttribute("data-filter");
      render();
    });
  }

  render();
})();

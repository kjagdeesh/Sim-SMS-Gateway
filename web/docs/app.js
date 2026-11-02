const pages = [...document.querySelectorAll("[data-doc-page]")];
const navLinks = [...document.querySelectorAll("[data-doc-link]")];
const sidebar = document.querySelector("#docs-sidebar");
const scrim = document.querySelector("#docs-scrim");
const search = document.querySelector("#docs-search");
const searchEmpty = document.querySelector("#search-empty");
const toast = document.querySelector("#docs-toast");
let toastTimer;

function showPage() {
    const route = location.hash.replace(/^#\/?/, "") || "overview";
    const page = pages.find((item) => item.id === route) ?? pages[0];
    for (const item of pages) item.hidden = item !== page;
    for (const link of navLinks) {
        if (link.dataset.docLink === page.id) link.setAttribute("aria-current", "page");
        else link.removeAttribute("aria-current");
    }

    const title = page.querySelector("h1")?.textContent ?? "Overview";
    document.querySelector("#current-title").textContent = title;
    document.title = `${title} - Sim SMS Gateway Documentation`;
    closeMenu();
    window.scrollTo({
        top: 0,
        behavior: "smooth"
    });
}

function closeMenu() {
    sidebar.classList.remove("open");
    scrim.hidden = true;
    document.querySelector("#menu-toggle").setAttribute("aria-expanded", "false");
}

document.querySelector("#menu-toggle").addEventListener("click", () => {
    sidebar.classList.add("open");
    scrim.hidden = false;
    document.querySelector("#menu-toggle").setAttribute("aria-expanded", "true");
});
scrim.addEventListener("click", closeMenu);
document.querySelector("#docs-nav").addEventListener("click", (event) => {
    if (event.target.closest("[data-doc-link]")) closeMenu();
});
window.addEventListener("hashchange", showPage);

function filterTopics() {
    const query = search.value.trim().toLocaleLowerCase();
    let visibleCount = 0;
    for (const link of navLinks) {
        const page = document.getElementById(link.dataset.docLink);
        const haystack = `${link.textContent} ${link.dataset.search} ${page?.textContent ?? ""}`.toLocaleLowerCase();
        link.hidden = Boolean(query) && !haystack.includes(query);
        if (!link.hidden) visibleCount++;
    }

    for (const group of document.querySelectorAll("[data-nav-group]")) {
        group.hidden = !group.querySelector("[data-doc-link]:not([hidden])");
    }
    searchEmpty.hidden = !query || visibleCount > 0;
}

search.addEventListener("input", filterTopics);
document.addEventListener("keydown", (event) => {
    if (event.key === "/" && !["INPUT", "TEXTAREA"].includes(document.activeElement.tagName)) {
        event.preventDefault();
        search.focus();
    } else if (event.key === "Escape") {
        search.value = "";
        filterTopics();
        closeMenu();
        search.blur();
    }
});

function setTheme(theme, persist) {
    document.documentElement.dataset.theme = theme;
    const button = document.querySelector("#theme-toggle");
    const dark = theme === "dark";
    button.setAttribute("aria-label", dark ? "Switch to light theme" : "Switch to dark theme");
    button.title = dark ? "Switch to light theme" : "Switch to dark theme";
    button.querySelector("i").className = `fa-solid fa-${dark ? "sun" : "moon"}`;
    if (persist) localStorage.setItem("simsms.docs.theme", theme);
}

const savedTheme = localStorage.getItem("simsms.docs.theme");
setTheme(savedTheme ?? (matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"), false);
document.querySelector("#theme-toggle").addEventListener("click", () => {
    setTheme(document.documentElement.dataset.theme === "dark" ? "light" : "dark", true);
});
matchMedia("(prefers-color-scheme: dark)").addEventListener("change", (event) => {
    if (!localStorage.getItem("simsms.docs.theme")) setTheme(event.matches ? "dark" : "light", false);
});

function showToast(message) {
    toast.textContent = message;
    toast.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
        toast.hidden = true;
    }, 2200);
}

document.addEventListener("click", async (event) => {
    const button = event.target.closest("[data-copy]");
    if (!button) return;

    const code = button.closest(".terminal")?.querySelector("pre");
    if (!code) return;
    try {
        await navigator.clipboard.writeText(code.textContent);
        button.querySelector("span").textContent = "Copied";
        showToast("Code copied to clipboard");
        setTimeout(() => {
            button.querySelector("span").textContent = "Copy";
        }, 1600);
    } catch {
        showToast("Copy failed - select the code and copy it manually");
    }
});

for (const group of document.querySelectorAll("[data-example-group]")) {
    const tabs = [...group.querySelectorAll("[data-example-tab]")];
    const panels = [...group.querySelectorAll("[data-example-panel]")];
    for (const tab of tabs) {
        tab.addEventListener("click", () => {
            for (const candidate of tabs) {
                candidate.setAttribute("aria-selected", String(candidate === tab));
            }
            for (const panel of panels) {
                panel.hidden = panel.dataset.examplePanel !== tab.dataset.exampleTab;
            }
        });
    }
}

filterTopics();
showPage();

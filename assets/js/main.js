
document.addEventListener("DOMContentLoaded", () => {
    // Theme toggle
    const themeBtn = document.getElementById("themeToggle");
    const body = document.body;
    
    // Load preference
    const savedTheme = localStorage.getItem("theme") || "light-mode";
    body.className = savedTheme;
    updateThemeIcon(savedTheme);

    if (themeBtn) {
        themeBtn.addEventListener("click", () => {
            const isLight = body.classList.contains("light-mode");
            const newTheme = isLight ? "dark-mode" : "light-mode";
            body.className = newTheme;
            localStorage.setItem("theme", newTheme);
            updateThemeIcon(newTheme);
        });
    }

    function updateThemeIcon(theme) {
        if (!themeBtn) return;
        if (theme === "dark-mode") {
            themeBtn.innerHTML = '<i class="bi bi-sun-fill"></i>';
        } else {
            themeBtn.innerHTML = '<i class="bi bi-moon-fill"></i>';
        }
    }

    // Code copy buttons
    document.querySelectorAll("pre").forEach(pre => {
        const btn = document.createElement("button");
        btn.className = "copy-btn";
        btn.innerHTML = '<i class="bi bi-clipboard"></i> Copy';
        btn.onclick = () => {
            navigator.clipboard.writeText(pre.innerText.replace('Copy', '').trim());
            btn.innerHTML = '<i class="bi bi-check2"></i> Copied!';
            setTimeout(() => btn.innerHTML = '<i class="bi bi-clipboard"></i> Copy', 2000);
        };
        pre.appendChild(btn);
    });
    
    // Search
    const searchInput = document.getElementById("searchInput");
    const searchResults = document.getElementById("searchResults");
    
    if (searchInput) {
        fetch('/search.json')
            .then(res => res.json())
            .then(data => {
                searchInput.addEventListener('input', (e) => {
                    const q = e.target.value.toLowerCase();
                    if (!q) { searchResults.innerHTML = ''; return; }
                    
                    const matches = data.filter(item => 
                        item.title.toLowerCase().includes(q) || 
                        item.content.toLowerCase().includes(q)
                    ).slice(0, 5);
                    
                    if (matches.length > 0) {
                        searchResults.innerHTML = '<div class="list-group position-absolute w-100 shadow" style="z-index: 1000;">' + 
                            matches.map(m => `<a href="${m.url}" class="list-group-item list-group-item-action"><strong>${m.title}</strong></a>`).join('') +
                        '</div>';
                    } else {
                        searchResults.innerHTML = '<div class="list-group position-absolute w-100 shadow" style="z-index: 1000;"><div class="list-group-item">No results found</div></div>';
                    }
                });
                
                // Hide on click outside
                document.addEventListener('click', (e) => {
                    if (e.target !== searchInput) searchResults.innerHTML = '';
                });
            });
    }
});

// Service Worker Registration
if ('serviceWorker' in navigator) {
    window.addEventListener('load', () => {
        navigator.serviceWorker.register('/service-worker.js');
    });
}

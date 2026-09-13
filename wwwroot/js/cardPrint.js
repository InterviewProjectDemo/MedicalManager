function escapeHtml(value) {
    return String(value ?? "")
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;");
}

function displayName(options) {
    return (options.patientName || "").trim() || "Name not on file";
}

function displayDob(options) {
    return (options.dateOfBirth || "").trim() || "Not on file";
}

function serializeChart(cardEl) {
    const svg = cardEl?.querySelector?.("svg.trend-svg");
    if (!svg) return "";

    const clone = svg.cloneNode(true);
    clone.classList.remove("trend-svg-monthly");
    clone.setAttribute("class", "print-chart");
    clone.setAttribute("preserveAspectRatio", "xMidYMid meet");
    clone.setAttribute("xmlns", "http://www.w3.org/2000/svg");
    clone.removeAttribute("aria-hidden");

    const hover = clone.querySelector(".chart-hover-line");
    hover?.remove();

    return new XMLSerializer().serializeToString(clone);
}

function renderIdentity(options) {
    return `<div class="identity-block">
        <div><span>Full name</span><strong>${escapeHtml(displayName(options))}</strong></div>
        <div><span>Date of birth</span><strong>${escapeHtml(displayDob(options))}</strong></div>
        ${options.periodLabel ? `<div><span>${escapeHtml(options.periodCaption || "Period")}</span><strong>${escapeHtml(options.periodLabel)}</strong></div>` : ""}
        <div><span>Prepared</span><strong>${escapeHtml(options.printedAt)}</strong></div>
    </div>`;
}

function renderRunningHeader(options) {
    return `<header class="doc-running-header">
        <div class="doc-running-brand">Medical Manager</div>
        <div class="doc-running-id">
            <strong>${escapeHtml(displayName(options))}</strong>
            <span>DOB: ${escapeHtml(displayDob(options))}</span>
        </div>
    </header>`;
}

function renderStats(stats) {
    if (!stats?.length) return "";
    const rows = stats.map((stat) => {
        const warn = stat.outOfRange ? " class=\"warn\"" : "";
        const sub = stat.subValue
            ? `<div class="sub">${escapeHtml(stat.subValue)}</div>`
            : "";
        return `<tr${warn}>
            <th>${escapeHtml(stat.label)}</th>
            <td>${escapeHtml(stat.value)}${sub}</td>
        </tr>`;
    }).join("");

    return `<section>
        <h2>Summary</h2>
        <table class="stats">
            <thead><tr><th>Measure</th><th>Value</th></tr></thead>
            <tbody>${rows}</tbody>
        </table>
    </section>`;
}

function renderTables(tables) {
    if (!tables?.length) return "";
    return tables.map((table) => {
        const columns = (table.columns || []).map((col) => `<th>${escapeHtml(col)}</th>`).join("");
        const body = (table.rows || []).map((row) => {
            const cells = (row.cells || []).map((cell) => `<td>${escapeHtml(cell)}</td>`).join("");
            return `<tr${row.highlight ? " class=\"warn\"" : ""}>${cells}</tr>`;
        }).join("");
        const empty = !table.rows?.length
            ? `<tr><td colspan="${Math.max(1, table.columns?.length || 1)}" class="empty">${escapeHtml(table.emptyMessage || "No data for this period.")}</td></tr>`
            : body;
        return `<section>
            <h2>${escapeHtml(table.heading || "Details")}</h2>
            <table>
                <thead><tr>${columns}</tr></thead>
                <tbody>${empty}</tbody>
            </table>
        </section>`;
    }).join("");
}

function renderChart(chartHtml, caption) {
    if (!chartHtml) return "";
    return `<section class="chart-section">
        <h2>${escapeHtml(caption || "Trend")}</h2>
        <div class="chart-frame">${chartHtml}</div>
        <p class="chart-note">Out-of-range values are shown in red.</p>
    </section>`;
}

function renderLegend(legend) {
    if (!legend?.length) return "";
    const items = legend.map((item) =>
        `<span><i style="background:${escapeHtml(item.color)}"></i>${escapeHtml(item.name)}</span>`).join("");
    return `<div class="legend">${items}</div>`;
}

function documentCss() {
    return `
        @page { margin: 1.05in 0.6in 0.7in 0.6in; }
        * { box-sizing: border-box; }
        body {
            margin: 0;
            color: #1f2a37;
            font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif;
            font-size: 12px;
            line-height: 1.4;
            padding-top: 0.15in;
        }
        .doc-running-header {
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 0.75rem;
            padding: 0.28in 0.05in 0.16in;
            background: #fff;
            border-bottom: 2px solid #0f3d5c;
            z-index: 20;
        }
        .doc-running-brand {
            font-size: 10px;
            font-weight: 800;
            letter-spacing: 0.08em;
            text-transform: uppercase;
            color: #0f766e;
        }
        .doc-running-id {
            display: flex;
            flex-wrap: wrap;
            justify-content: flex-end;
            gap: 0.15rem 0.75rem;
            font-size: 11px;
            color: #0f3d5c;
        }
        .doc-running-id strong { font-size: 12px; }
        .brand {
            padding: 0.15rem 0 0.7rem;
            border-bottom: 3px solid #0f3d5c;
            margin: 0.15in 0 0.7rem;
        }
        .brand-mark {
            font-size: 11px;
            font-weight: 800;
            letter-spacing: 0.08em;
            text-transform: uppercase;
            color: #0f766e;
        }
        h1 {
            margin: 0.15rem 0 0;
            font-size: 20px;
            color: #0f3d5c;
        }
        .subtitle { margin: 0.2rem 0 0; color: #475467; }
        .identity-block {
            display: grid;
            grid-template-columns: repeat(2, minmax(0, 1fr));
            gap: 0.55rem;
            margin: 0 0 1rem;
        }
        .identity-block div {
            border: 1px solid #d0d5dd;
            border-radius: 8px;
            padding: 0.45rem 0.6rem;
            background: #f0fdfa;
        }
        .identity-block span { display: block; font-size: 10px; text-transform: uppercase; letter-spacing: 0.04em; color: #667085; }
        .identity-block strong { font-size: 13px; color: #0f3d5c; }
        h2 {
            margin: 0 0 0.4rem;
            font-size: 13px;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            color: #0f766e;
        }
        section { margin-bottom: 1rem; }
        table { width: 100%; border-collapse: collapse; }
        th, td {
            border: 1px solid #d0d5dd;
            padding: 0.38rem 0.5rem;
            text-align: left;
            vertical-align: top;
        }
        thead th {
            background: #0f3d5c;
            color: #fff;
            font-size: 11px;
            text-transform: uppercase;
            letter-spacing: 0.03em;
        }
        table.stats thead th { background: #0f766e; }
        tbody tr:nth-child(even) { background: #f8fafc; }
        tr.warn td, tr.warn th { color: #b42318; font-weight: 700; }
        .sub { font-size: 10px; font-weight: 600; color: inherit; }
        .empty { text-align: center; color: #667085; }
        .chart-frame {
            border: 1px solid #d0d5dd;
            border-radius: 8px;
            padding: 0.4rem;
            background: #fff;
        }
        .print-chart { width: 100%; height: 240px; display: block; }
        .chart-grid { stroke: #d0d5dd; stroke-width: 1; }
        .chart-axis { font-size: 11px; fill: #344054; font-family: "Segoe UI", Arial, sans-serif; }
        .chart-note, .disclaimer {
            margin: 0.35rem 0 0;
            color: #667085;
            font-size: 11px;
        }
        .legend { display: flex; gap: 1rem; margin: 0.35rem 0 0.55rem; color: #344054; }
        .legend i { width: 9px; height: 9px; border-radius: 50%; display: inline-block; margin-right: 0.3rem; }
        footer {
            margin-top: 1.2rem;
            padding-top: 0.6rem;
            border-top: 1px solid #d0d5dd;
        }
        @media print {
            body { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
            thead { display: table-header-group; }
            tr, section { break-inside: avoid; }
            .doc-running-header {
                position: fixed;
                top: 0;
                left: 0;
                right: 0;
            }
        }
    `;
}

function buildHtml(options, chartHtml) {
    return `<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8" />
    <title>${escapeHtml(options.title)} — ${escapeHtml(displayName(options))} — Medical Manager</title>
    <style>${documentCss()}</style>
</head>
<body>
    ${renderRunningHeader(options)}
    <header class="brand">
        <div class="brand-mark">Medical Manager</div>
        <h1>${escapeHtml(options.title)}</h1>
        ${options.subtitle ? `<p class="subtitle">${escapeHtml(options.subtitle)}</p>` : ""}
    </header>
    ${renderIdentity(options)}
    ${renderStats(options.stats)}
    ${renderChart(chartHtml, options.chartCaption)}
    ${renderLegend(options.legend)}
    ${renderTables(options.tables)}
    <footer>
        <p class="disclaimer">For personal records. This printout is not a medical diagnosis and should not replace professional care.</p>
    </footer>
</body>
</html>`;
}

function fileSafe(value) {
    return String(value || "snapshot")
        .replace(/[—–]/g, "-")
        .replace(/[^A-Za-z0-9]+/g, "-")
        .replace(/^-+|-+$/g, "")
        .slice(0, 60);
}

function buildFilename(options, extension) {
    const stamp = new Date().toISOString().slice(0, 10);
    return `MedicalManager-${fileSafe(options.title)}-${fileSafe(displayName(options))}-${stamp}.${extension}`;
}

function compactSummary(options) {
    const stats = (options.stats || [])
        .map((stat) => `${stat.label}: ${stat.value}${stat.subValue ? ` (${stat.subValue})` : ""}`)
        .join(" · ");
    const lines = [
        "Medical Manager",
        options.title,
        `Name: ${displayName(options)}`,
        `DOB: ${displayDob(options)}`
    ];
    if (options.periodLabel) {
        lines.push(`${options.periodCaption || "Period"}: ${options.periodLabel}`);
    }
    if (stats) lines.push(stats);
    lines.push("For personal records.");
    return lines.join("\n");
}

function emailBody(options) {
    return [
        compactSummary(options),
        "",
        "A Medical Manager snapshot was downloaded to this device.",
        "Please attach that HTML file to this email. To send a PDF instead, open Print and choose Save as PDF, then attach the PDF.",
        "",
        "This message is for personal records and is not a medical diagnosis."
    ].join("\n");
}

function smsBody(options) {
    const stats = (options.stats || [])
        .slice(0, 4)
        .map((stat) => `${stat.label} ${stat.value}`)
        .join(" · ");
    const parts = [
        `Medical Manager: ${options.title}`,
        `Name ${displayName(options)} · DOB ${displayDob(options)}`
    ];
    if (options.periodLabel) parts.push(String(options.periodLabel));
    if (stats) parts.push(stats);
    parts.push("Attach the downloaded snapshot. For personal records.");
    return parts.join("\n").slice(0, 480);
}

function downloadDocument(html, filename) {
    const blob = new Blob([html], { type: "text/html;charset=utf-8" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = filename;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 4000);
}

function printHtml(html) {
    const frame = document.createElement("iframe");
    frame.setAttribute("aria-hidden", "true");
    frame.style.position = "fixed";
    frame.style.right = "0";
    frame.style.bottom = "0";
    frame.style.width = "0";
    frame.style.height = "0";
    frame.style.border = "0";
    document.body.appendChild(frame);

    const doc = frame.contentDocument;
    if (!doc) {
        frame.remove();
        const win = window.open("", "_blank");
        if (!win) return;
        win.document.open();
        win.document.write(html);
        win.document.close();
        win.focus();
        win.print();
        return;
    }

    doc.open();
    doc.write(html);
    doc.close();

    const cleanup = () => frame.remove();
    frame.contentWindow?.addEventListener("afterprint", cleanup);
    setTimeout(() => {
        frame.contentWindow?.focus();
        frame.contentWindow?.print();
        setTimeout(cleanup, 2000);
    }, 250);
}

function closeShareDialog() {
    document.getElementById("card-share-dialog")?.remove();
}

function showShareDialog({ html, options }) {
    closeShareDialog();

    const backdrop = document.createElement("div");
    backdrop.id = "card-share-dialog";
    backdrop.className = "card-share-dialog";
    backdrop.innerHTML = `
        <div class="card-share-panel" role="dialog" aria-modal="true" aria-labelledby="card-share-title">
            <p class="card-share-kicker">Medical Manager</p>
            <h2 id="card-share-title">Share this snapshot</h2>
            <p class="card-share-identity">
                <strong>${escapeHtml(displayName(options))}</strong>
                <span>DOB ${escapeHtml(displayDob(options))}</span>
            </p>
            <p class="card-share-lead">${escapeHtml(options.title)}</p>
            <div class="card-share-actions">
                <button type="button" class="card-share-action" data-share="print">
                    <span>Print</span>
                    <small>Send to a connected printer or Save as PDF</small>
                </button>
                <button type="button" class="card-share-action" data-share="email">
                    <span>Email</span>
                    <small>Download the document, then open email to attach it</small>
                </button>
                <button type="button" class="card-share-action" data-share="text">
                    <span>Text</span>
                    <small>Download the document and open a text message</small>
                </button>
            </div>
            <button type="button" class="card-share-cancel" data-share="cancel">Cancel</button>
        </div>
    `;

    const onKey = (event) => {
        if (event.key === "Escape") {
            event.preventDefault();
            teardown();
        }
    };

    const teardown = () => {
        document.removeEventListener("keydown", onKey);
        backdrop.remove();
    };

    backdrop.addEventListener("click", (event) => {
        if (event.target === backdrop) teardown();
    });

    backdrop.querySelectorAll("[data-share]").forEach((button) => {
        button.addEventListener("click", () => {
            const action = button.getAttribute("data-share");
            if (action === "cancel") {
                teardown();
                return;
            }
            if (action === "print") {
                teardown();
                printHtml(html);
                return;
            }
            if (action === "email") {
                downloadDocument(html, buildFilename(options, "html"));
                const subject = `Medical Manager: ${options.title} — ${displayName(options)}`;
                window.location.href = `mailto:?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(emailBody(options))}`;
                teardown();
                return;
            }
            if (action === "text") {
                downloadDocument(html, buildFilename(options, "html"));
                window.location.href = `sms:?body=${encodeURIComponent(smsBody(options))}`;
                teardown();
            }
        });
    });

    document.addEventListener("keydown", onKey);
    document.body.appendChild(backdrop);
    backdrop.querySelector("[data-share='print']")?.focus();
}

export function printCard(cardEl, options) {
    const printedAt = new Date().toLocaleString(undefined, {
        year: "numeric",
        month: "short",
        day: "numeric",
        hour: "numeric",
        minute: "2-digit"
    });
    const payload = { ...(options || {}), printedAt };
    const chartHtml = payload.includeChart ? serializeChart(cardEl) : "";
    const html = buildHtml(payload, chartHtml);
    showShareDialog({ html, options: payload });
}

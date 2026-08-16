/* iHateCards desktop bridge: render each print page to a high-DPI RGB raster
   and hand it to the Python backend, which outputs a true CMYK (SWOP) PDF.
   Reuses the layout helpers already defined in the page's main script. */
(function () {
    var EXPORT_DPI = 300;

    function loadImg(src) {
        return new Promise(function (res) {
            var i = new Image();
            i.onload = function () { res(i); };
            i.onerror = function () { res(null); };
            i.src = src;
        });
    }

    function drawFitted(ctx, img, dx, dy, dw, dh, nw, nh, cellWmm, cellHmm) {
        var autoRot = needsAutoRotate(nw, nh, cellWmm, cellHmm);
        ctx.save();
        ctx.beginPath();
        ctx.rect(dx, dy, dw, dh);
        ctx.clip();
        if (state.fitImage) { ctx.fillStyle = '#fff'; ctx.fillRect(dx, dy, dw, dh); }
        ctx.translate(dx + dw / 2, dy + dh / 2);
        if (autoRot) ctx.rotate(Math.PI / 2);
        var boxW = autoRot ? dh : dw, boxH = autoRot ? dw : dh;
        var ar = nw / nh, bar = boxW / boxH, drawW, drawH;
        if (state.fitImage) {
            if (ar > bar) { drawW = boxW; drawH = boxW / ar; } else { drawH = boxH; drawW = boxH * ar; }
        } else {
            if (ar > bar) { drawH = boxH; drawW = boxH * ar; } else { drawW = boxW; drawH = boxW / ar; }
        }
        ctx.imageSmoothingEnabled = true;
        ctx.imageSmoothingQuality = 'high';
        ctx.drawImage(img, -drawW / 2, -drawH / 2, drawW, drawH);
        ctx.restore();
    }

    async function renderExportCanvas(pageIndex, side, dpi) {
        var paper = getPaper();
        var isBack = (side === 'back');
        var scale = dpi / 25.4;
        var cv = document.createElement('canvas');
        cv.width = Math.round(paper.width * scale);
        cv.height = Math.round(paper.height * scale);
        var ctx = cv.getContext('2d');
        ctx.fillStyle = '#fff';
        ctx.fillRect(0, 0, cv.width, cv.height);

        var calib = activeCalib(side);
        if (calib.angle) {
            ctx.translate(cv.width / 2, cv.height / 2);
            ctx.rotate(calib.angle * Math.PI / 180);
            ctx.translate(-cv.width / 2, -cv.height / 2);
        }

        var margin = getEffectiveMargin();
        var instrH = getInstructionHeight();
        var workW = paper.width - margin * 2;
        var workH = paper.height - margin * 2 - instrH;
        var usedW = state.cardsPerRow * state.cardWidth;
        var usedH = state.cardsPerCol * state.cardHeight;
        var baseX = margin + (workW - usedW) / 2;
        var baseY = margin + instrH + (workH - usedH) / 2;
        if (isBack) { baseX += state.offsetX; baseY += state.offsetY; }
        baseX += calib.dx; baseY += calib.dy;

        var instrText = getInstructionText();
        if (instrText) {
            ctx.fillStyle = '#666';
            ctx.font = (3.2 * scale) + 'px sans-serif';
            ctx.textAlign = 'center';
            ctx.fillText(instrText, paper.width / 2 * scale, (margin + 5) * scale);
        }

        var expanded = getExpandedImages();
        var startIdx = pageIndex * state.cardsPerPage;
        var pageImages = expanded.slice(startIdx, startIdx + state.cardsPerPage);
        var cutW = getCutWidth(), cutH = getCutHeight();
        var filled = [];

        for (var row = 0; row < state.cardsPerCol; row++) {
            for (var col = 0; col < state.cardsPerRow; col++) {
                var idx = row * state.cardsPerRow + col;
                var displayCol = isBack ? (state.cardsPerRow - 1 - col) : col;
                var data = null, nw = 0, nh = 0;
                if (isBack && idx < pageImages.length) {
                    var bd = state.individualBacks ? (pageImages[idx].backImage || null) : state.backImage;
                    if (bd) { data = (bd._origUrl || bd.dataUrl); nw = bd.naturalWidth; nh = bd.naturalHeight; }
                } else if (!isBack && idx < pageImages.length) {
                    var fd = pageImages[idx];
                    data = (fd._origUrl || fd.dataUrl); nw = fd.naturalWidth; nh = fd.naturalHeight;
                }
                if (!data) continue;
                var x = baseX + displayCol * state.cardWidth;
                var y = baseY + row * state.cardHeight;
                var img = await loadImg(data);
                if (img) {
                    if (state.polaroidMode) {
                        ctx.fillStyle = '#fff';
                        ctx.fillRect(x * scale, y * scale, state.cardWidth * scale, state.cardHeight * scale);
                        var photo = getPolaroidPhotoArea();
                        drawFitted(ctx, img, (x + state.polaroidSide) * scale, (y + state.polaroidTop) * scale,
                            photo.w * scale, photo.h * scale, nw, nh, photo.w, photo.h);
                    } else {
                        drawFitted(ctx, img, x * scale, y * scale, state.cardWidth * scale, state.cardHeight * scale,
                            nw, nh, state.cardWidth, state.cardHeight);
                    }
                }
                filled.push({ displayCol: displayCol, row: row });
            }
        }

        if (state.showCropMarks && filled.length) {
            ctx.strokeStyle = '#000';
            ctx.lineWidth = Math.max(1, 0.2 * scale);
            ctx.lineCap = 'butt';
            for (var k = 0; k < filled.length; k++) {
                var s = filled[k];
                var mx = baseX + s.displayCol * state.cardWidth + state.bleed;
                var my = baseY + s.row * state.cardHeight + state.bleed;
                var segs = buildCropMarks(mx, my, cutW, cutH, s.row, s.displayCol,
                    state.cardsPerCol, state.cardsPerRow, paper.width, paper.height);
                ctx.beginPath();
                for (var q = 0; q < segs.length; q++) {
                    var seg = segs[q];
                    if (seg.dir === 'h') {
                        ctx.moveTo(seg.x * scale, seg.y * scale);
                        ctx.lineTo((seg.x + seg.len) * scale, seg.y * scale);
                    } else {
                        ctx.moveTo(seg.x * scale, seg.y * scale);
                        ctx.lineTo(seg.x * scale, (seg.y + seg.len) * scale);
                    }
                }
                ctx.stroke();
            }
        }

        return cv.toDataURL('image/png');
    }

    async function buildPages() {
        var pages = [];
        var paper = getPaper();
        if (state.duplexMode && hasAnyBack()) {
            for (var i = 0; i < state.totalPages; i++) {
                pages.push({ dataUrl: await renderExportCanvas(i, 'front', EXPORT_DPI), w_mm: paper.width, h_mm: paper.height });
                pages.push({ dataUrl: await renderExportCanvas(i, 'back', EXPORT_DPI), w_mm: paper.width, h_mm: paper.height });
            }
        } else {
            for (var j = 0; j < state.totalPages; j++) {
                pages.push({ dataUrl: await renderExportCanvas(j, 'front', EXPORT_DPI), w_mm: paper.width, h_mm: paper.height });
            }
        }
        return pages;
    }

    // Persisted print settings (saved via the Python backend → survives restart).
    var prnSettings = {};
    var PRN_TCP_HOSTS = [];
    async function loadPrnSettings() {
        try {
            var r = await window.pywebview.api.load_settings();
            if (r && r.ok && r.settings && r.settings.print) prnSettings = r.settings.print;
        } catch (e) {}
        try {
            var h = await window.pywebview.api.tcp_hosts();
            if (h && h.ok && h.hosts) PRN_TCP_HOSTS = h.hosts;
        } catch (e) {}
    }
    async function savePrnSettings() {
        try {
            var r = await window.pywebview.api.load_settings();
            var all = (r && r.ok && r.settings) ? r.settings : {};
            all.print = prnSettings;
            await window.pywebview.api.save_settings(all);
        } catch (e) {}
    }

    function esc(v) { return String(v).replace(/"/g, '&quot;'); }

    function showPrintDialog(printers, def) {
        return new Promise(function (resolve) {
            var s = prnSettings || {};
            var paper = getPaper();
            var duplex = !!(state.duplexMode && hasAnyBack());
            var mode = s.scaleMode || 'real';
            var pct = (s.scalePct != null ? s.scalePct : 100);
            var dpi = (s.dpi != null ? s.dpi : 300);
            var psMap = s.printerPS || {};
            function psGuess(name) { return /xerox|altalink|postscript|adobe pdf|\bps\b/i.test(name || ''); }
            function psFor(name) { return (psMap[name] != null) ? psMap[name] : psGuess(name); }
            var curPs = psFor(s.printer || def);
            var ipMap = s.printerIP || {};
            function ipFor(name) { return ipMap[name] || ''; }
            var curIp = ipFor(s.printer || def);
            var hostHint = (PRN_TCP_HOSTS && PRN_TCP_HOSTS.length) ? ('найдены: ' + PRN_TCP_HOSTS.join(', ')) : '';
            var inp = 'background:var(--bgC);border:1px solid var(--brd);border-radius:6px;color:var(--t1);padding:6px;';
            var prinOpts = printers.map(function (p) {
                return '<option value="' + esc(p) + '"' + (((s.printer || def) === p) ? ' selected' : '') + '>' + p + '</option>';
            }).join('');
            var ov = document.createElement('div');
            ov.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.6);z-index:2000;display:flex;align-items:center;justify-content:center;';
            var col = 'display:flex;flex-direction:column;gap:11px;';
            var box = document.createElement('div');
            box.style.cssText = 'background:var(--bg2);border:1px solid var(--brd);border-radius:10px;padding:18px;max-width:92vw;max-height:92vh;overflow:auto;display:flex;flex-direction:column;gap:14px;font-family:\'Century Gothic\',\'Nunito\',sans-serif;color:var(--t1);font-size:13px;';
            box.innerHTML =
                '<div style="font-weight:700;font-size:15px;">Печать</div>'
                + '<div id="pdRow" style="display:flex;gap:18px;align-items:flex-start;">'
                + '<div style="width:330px;' + col + '">'
                +   '<label>Принтер<br><select id="pdPrinter" style="width:100%;margin-top:4px;' + inp + '">' + prinOpts + '</select></label>'
                +   '<button id="pdProps" class="btn btn-secondary" style="padding:8px;">Параметры принтера…</button>'
                +   '<label>Копии: <input id="pdCopies" type="number" min="1" max="999" value="' + (s.copies || 1) + '" style="width:72px;' + inp + '"></label>'
                +   '<div><div style="margin-bottom:6px;color:var(--t2);font-weight:700;">Размер</div>'
                +     '<label style="display:block;margin:3px 0;"><input type="radio" name="pdMode" value="real" ' + (mode === 'real' ? 'checked' : '') + '> Реальный размер</label>'
                +     '<label style="display:block;margin:3px 0;"><input type="radio" name="pdMode" value="fit" ' + (mode === 'fit' ? 'checked' : '') + '> Подогнать (под размер бумаги принтера)</label>'
                +     '<label style="display:block;margin:3px 0;"><input type="radio" name="pdMode" value="percent" ' + (mode === 'percent' ? 'checked' : '') + '> Свой размер: <input id="pdPct" type="number" min="1" max="400" step="0.01" value="' + pct + '" style="width:84px;' + inp + '"> %</label>'
                +     '<div style="display:flex;gap:6px;margin-top:6px;"><button id="pd70" class="btn btn-secondary" style="padding:4px 12px;">70%</button><button id="pd66" class="btn btn-secondary" style="padding:4px 12px;">66%</button></div></div>'
                +   '<button id="pdAdvBtn" class="btn btn-secondary" style="padding:7px 10px;text-align:left;"><span id="pdAdvArrow">▸</span> Расширенные настройки</button>'
                + '</div>'
                + '<div id="pdAdv" style="width:320px;display:none;flex-direction:column;gap:11px;border-left:1px solid var(--brd);padding-left:18px;">'
                +   '<label style="display:flex;align-items:center;gap:8px;"><input id="pdBw" type="checkbox" ' + (s.bw ? 'checked' : '') + '> Чёрно-белая печать</label>'
                +   '<label style="display:flex;align-items:center;gap:8px;"><input id="pdToner" type="checkbox" ' + (s.toner ? 'checked' : '') + '> Экономия тонера</label>'
                +   '<label>Качество печати: <select id="pdDpi" style="' + inp + '">'
                +   ['<option value="360"' + (dpi == 360 ? ' selected' : '') + '>Высокое (360 dpi)</option>',
                     '<option value="300"' + (dpi == 300 ? ' selected' : '') + '>Стандарт (300 dpi)</option>',
                     '<option value="200"' + (dpi == 200 ? ' selected' : '') + '>Эконом (200 dpi)</option>',
                     '<option value="150"' + (dpi == 150 ? ' selected' : '') + '>Черновик (150 dpi)</option>'].join('')
                +   '</select></label>'
                +   '<div style="color:var(--t2);font-size:11px;">Ниже dpi — меньше размер задания (помогает при «ошибке порта» WSD).</div>'
                +   '<label style="display:flex;align-items:center;gap:8px;"><input id="pdPs" type="checkbox" ' + (curPs ? 'checked' : '') + '> Принтер PostScript — печать напрямую в CMYK</label>'
                +   '<div style="color:var(--t2);font-size:11px;">Выкл. — печать через драйвер (универсально: EPSON и любой принтер). Вкл. — только для PostScript-принтеров (Xerox); иначе иероглифы.</div>'
                +   '<div id="pdIpRow" style="display:' + (curPs ? 'block' : 'none') + ';">'
                +     '<label>IP принтера (порт 9100): <input id="pdIp" value="' + esc(curIp) + '" placeholder="напр. 192.168.1.3" style="width:150px;' + inp + '"></label>'
                +     '<div style="color:var(--t2);font-size:11px;">Нужно, если PostScript-принтер на порту WSD. ' + hostHint + '</div></div>'
                +   '<div style="color:var(--t2);font-size:12px;">Стороны: ' + (duplex ? 'двухсторонняя' : 'односторонняя') + ' (по настройке приложения)</div>'
                + '</div>'
                + '</div>'
                + '<div style="display:flex;gap:8px;justify-content:flex-end;"><button id="pdCancel" class="btn btn-secondary" style="padding:8px 14px;">Отмена</button><button id="pdOk" class="btn" style="padding:8px 14px;">Печать</button></div>';
            ov.appendChild(box);
            document.body.appendChild(ov);
            function q(id) { return box.querySelector(id); }
            function setMode(m) {
                var rs = box.querySelectorAll('input[name=pdMode]');
                for (var i = 0; i < rs.length; i++) rs[i].checked = (rs[i].value === m);
            }
            function setAdv(open) {
                q('#pdAdv').style.display = open ? 'flex' : 'none';
                q('#pdAdvArrow').textContent = open ? '▾' : '▸';
            }
            q('#pdAdvBtn').onclick = function (e) {
                e.preventDefault();
                setAdv(q('#pdAdv').style.display === 'none');
            };
            // open advanced automatically if any advanced option is non-default
            if (s.bw || s.toner || curPs || (s.dpi != null && s.dpi != 300)) setAdv(true);
            q('#pd70').onclick = function (e) { e.preventDefault(); setMode('percent'); q('#pdPct').value = '70'; };
            q('#pd66').onclick = function (e) { e.preventDefault(); setMode('percent'); q('#pdPct').value = '66'; };
            q('#pdPct').onfocus = function () { setMode('percent'); };
            function autofillIp() {
                if (!q('#pdPs').checked) return;
                if ((q('#pdIp').value || '').trim()) return;
                var n = q('#pdPrinter').value;
                try {
                    window.pywebview.api.printer_ip(n).then(function (r) {
                        if (r && r.ok && r.ip && !(q('#pdIp').value || '').trim()) q('#pdIp').value = r.ip;
                    }).catch(function () {});
                } catch (e) {}
            }
            q('#pdPrinter').onchange = function () {
                var n = q('#pdPrinter').value;
                q('#pdPs').checked = psFor(n);
                q('#pdIp').value = ipFor(n);
                q('#pdIpRow').style.display = q('#pdPs').checked ? 'block' : 'none';
                autofillIp();
            };
            q('#pdPs').onchange = function () {
                q('#pdIpRow').style.display = q('#pdPs').checked ? 'block' : 'none';
                autofillIp();
            };
            autofillIp();
            q('#pdProps').onclick = async function (e) {
                e.preventDefault();
                try { await window.pywebview.api.printer_properties(q('#pdPrinter').value); } catch (_) {}
            };
            q('#pdCancel').onclick = function () { ov.remove(); resolve(null); };
            q('#pdOk').onclick = function () {
                var m = box.querySelector('input[name=pdMode]:checked').value;
                var pct2 = parseFloat(q('#pdPct').value) || 100;
                var scale = (m === 'percent') ? pct2 / 100 : 1.0;
                var dpiVal = parseInt(q('#pdDpi').value, 10) || 300;
                var ipVal = (q('#pdIp').value || '').trim();
                var opts = {
                    printer: q('#pdPrinter').value,
                    copies: Math.max(1, parseInt(q('#pdCopies').value, 10) || 1),
                    scaleMode: m, scalePct: pct2, scale: scale, fit: (m === 'fit'),
                    bw: q('#pdBw').checked, toner: q('#pdToner').checked,
                    postscript: q('#pdPs').checked, dpi: dpiVal, ip: ipVal,
                    duplex: duplex, tumble: false
                };
                psMap[opts.printer] = opts.postscript;
                ipMap[opts.printer] = ipVal;
                prnSettings = {
                    printer: opts.printer, copies: opts.copies, scaleMode: m,
                    scalePct: pct2, bw: opts.bw, toner: opts.toner, dpi: dpiVal,
                    printerPS: psMap, printerIP: ipMap
                };
                savePrnSettings();
                ov.remove();
                resolve(opts);
            };
        });
    }

    async function printViaApp() {
        var overlay = document.getElementById('loadingOverlay');
        try {
            var pl = await window.pywebview.api.list_printers();
            if (!pl || !pl.ok || !pl.printers.length) { alert('Принтеры не найдены'); return; }
            var opts = await showPrintDialog(pl.printers, pl.default);
            if (!opts) return;
            if (overlay) overlay.classList.add('active');
            var pages = await buildPages();
            var res = await window.pywebview.api.print_cmyk(pages, opts);
            if (res && res.ok) alert('Отправлено на печать (' + (res.mode || '') + '):\n' + res.printer);
            else if (res) alert('Ошибка печати: ' + res.error);
        } catch (e) {
            alert('Ошибка: ' + e);
        } finally {
            if (overlay) overlay.classList.remove('active');
        }
    }

    async function exportViaApp() {
        var overlay = document.getElementById('loadingOverlay');
        if (overlay) overlay.classList.add('active');
        try {
            var pages = await buildPages();
            var res = await window.pywebview.api.export_cmyk(pages, 'cards.pdf');
            if (res && res.ok) {
                alert('Сохранено:\n' + res.path);
            } else if (res && res.error && res.error !== 'cancelled') {
                alert('Ошибка экспорта: ' + res.error);
            }
        } catch (e) {
            alert('Ошибка: ' + e);
        } finally {
            if (overlay) overlay.classList.remove('active');
        }
    }

    async function saveCalibViaApp() {
        try {
            if (window.__ensureJsPDF) await window.__ensureJsPDF();
            var jsPDF = window.jspdf.jsPDF;
            var p = getPaper();
            var pdf = new jsPDF({ orientation: 'portrait', unit: 'mm', format: [p.width, p.height] });
            drawCalibTarget(pdf, 'ЛИЦО');
            pdf.addPage([p.width, p.height], 'portrait');
            drawCalibTarget(pdf, 'ОБОРОТ');
            var uri = pdf.output('datauristring');
            var res = await window.pywebview.api.save_pdf(uri, 'calibration-' + state.paperSize + '-duplex.pdf');
            if (res && res.ok) {
                alert('Сохранено:\n' + res.path);
            } else if (res && res.error && res.error !== 'cancelled') {
                alert('Ошибка сохранения: ' + res.error);
            }
        } catch (e) {
            alert('Ошибка: ' + e);
        }
    }

    function hook() {
        var inApp = !!(window.pywebview && window.pywebview.api);
        var btn = document.getElementById('exportBtn');
        if (btn) {
            if (inApp) {
                btn.title = 'Экспорт в CMYK US Web Coated (SWOP), PDF/X-1a';
            }
            // Capture-phase interceptor: when running inside the desktop app, take
            // over the export and route it through the CMYK backend.
            btn.addEventListener('click', function (e) {
                if (window.pywebview && window.pywebview.api) {
                    e.stopImmediatePropagation();
                    e.preventDefault();
                    exportViaApp();
                }
            }, true);
        }
        // Route the calibration target through a native "Save As" dialog too.
        var cbtn = document.getElementById('calibTargetBtn');
        if (cbtn) {
            cbtn.addEventListener('click', function (e) {
                if (window.pywebview && window.pywebview.api) {
                    e.stopImmediatePropagation();
                    e.preventDefault();
                    saveCalibViaApp();
                }
            }, true);
        }
        // Native CMYK print: bypass window.print() (web/RGB) entirely.
        var prn = document.getElementById('printBtn');
        if (prn) {
            prn.addEventListener('click', function (e) {
                if (window.pywebview && window.pywebview.api) {
                    e.stopImmediatePropagation();
                    e.preventDefault();
                    printViaApp();
                }
            }, true);
        }
    }

    function markCmykLabel() {
        var btn = document.getElementById('exportBtn');
        if (btn && window.pywebview && window.pywebview.api) {
            btn.title = 'Экспорт в CMYK US Web Coated (SWOP), PDF/X-1a';
        }
        var prn = document.getElementById('printBtn');
        if (prn && window.pywebview && window.pywebview.api) {
            prn.textContent = 'Печать (CMYK)';
            prn.title = 'Прямая печать в CMYK (PostScript) на выбранный принтер, без RGB';
        }
        var pb = document.getElementById('softproofBtn');
        if (pb && window.pywebview && window.pywebview.api) pb.style.display = 'flex';
    }

    // ---- Large-format import (TIFF/BMP/PSD...) decoded in Python, offline ----
    function isBigRaster(file) {
        var n = (file.name || '').toLowerCase();
        return file.type === 'image/tiff' || /\.(tif|tiff|bmp|psd|tga|jp2|j2k)$/.test(n);
    }
    if (typeof fileToDataUrls === 'function') {
        var _origFileToDataUrls = fileToDataUrls;
        fileToDataUrls = async function (file) {
            if (window.pywebview && window.pywebview.api && isBigRaster(file)) {
                var durl = await readFileDataUrl(file);
                var res = await window.pywebview.api.decode_raster(durl, file.name || '');
                if (res && res.ok) return res.images;
                alert('Не удалось декодировать ' + (file.name || 'файл') + (res && res.error ? (': ' + res.error) : ''));
                return [];
            }
            return _origFileToDataUrls(file);
        };
    }

    // ---- CMYK SWOP soft-proof for the on-screen preview ----
    var softproofOn = false;

    function collectImageObjs() {
        var arr = [];
        state.images.forEach(function (im) { arr.push(im); if (im.backImage) arr.push(im.backImage); });
        if (state.backImage) arr.push(state.backImage);
        return arr;
    }

    async function applySoftproof(on) {
        var overlay = document.getElementById('loadingOverlay');
        var objs = collectImageObjs();
        if (on) {
            if (overlay) overlay.classList.add('active');
            try {
                for (var i = 0; i < objs.length; i++) {
                    var im = objs[i];
                    if (!im._origUrl) im._origUrl = im.dataUrl;
                    if (!im._proofUrl) {
                        var r = await window.pywebview.api.softproof(im._origUrl);
                        if (r && r.ok) im._proofUrl = r.dataUrl;
                    }
                    if (im._proofUrl) im.dataUrl = im._proofUrl;
                }
            } finally { if (overlay) overlay.classList.remove('active'); }
        } else {
            objs.forEach(function (im) { if (im._origUrl) im.dataUrl = im._origUrl; });
        }
        renderPage();
        renderImageList();
        if (typeof renderBackPreview === 'function') renderBackPreview();
    }

    // Re-apply soft-proof to images added while the toggle is on.
    if (typeof addImages === 'function') {
        var _origAddImages = addImages;
        addImages = async function (files) {
            await _origAddImages(files);
            if (softproofOn) await applySoftproof(true);
        };
    }
    if (typeof setBackImage === 'function') {
        var _origSetBackImage = setBackImage;
        setBackImage = async function (file) {
            await _origSetBackImage(file);
            if (softproofOn) await applySoftproof(true);
        };
    }

    function updateProofBtn() {
        var b = document.getElementById('softproofBtn');
        if (!b) return;
        b.textContent = softproofOn ? 'Просм. CMYK' : 'Просм. RGB';
        b.classList.toggle('active', softproofOn);
    }

    function hookSoftproof() {
        var b = document.getElementById('softproofBtn');
        if (!b) return;
        updateProofBtn();
        b.addEventListener('click', async function () {
            if (!(window.pywebview && window.pywebview.api)) return;
            softproofOn = !softproofOn;
            updateProofBtn();
            await applySoftproof(softproofOn);
        });
    }

    function initBridge() {
        hook();
        hookSoftproof();
        markCmykLabel();
        if (window.pywebview && window.pywebview.api) loadPrnSettings();
    }
    if (document.readyState !== 'loading') initBridge();
    else document.addEventListener('DOMContentLoaded', initBridge);
    // window.pywebview may become ready after the DOM; relabel/reveal once it is.
    window.addEventListener('pywebviewready', markCmykLabel);
})();

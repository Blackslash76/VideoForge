// ─── Montage Module ──────────────────────────────────
let montageFiles = [];
let montageAudioFiles = [];
let selectedStyle = 0;
let montagePollIntervals = {};

function initMontage() {
    initMontageUpload();
    initStyleSelector();
    loadMontages();
    setInterval(loadMontages, 3000);
}

// ─── Photo Upload ────────────────────────────────────
function initMontageUpload() {
    const zone = document.getElementById('montageUploadZone');
    const input = document.getElementById('montageFileInput');

    zone.addEventListener('dragover', e => { e.preventDefault(); zone.classList.add('dragover'); });
    zone.addEventListener('dragleave', () => zone.classList.remove('dragover'));
    zone.addEventListener('drop', e => {
        e.preventDefault();
        zone.classList.remove('dragover');
        addMontageFiles(Array.from(e.dataTransfer.files));
    });
    input.addEventListener('change', e => addMontageFiles(Array.from(e.target.files)));
}

function addMontageFiles(files) {
    const supported = ['jpg', 'jpeg', 'png', 'bmp', 'gif', 'tiff', 'tif', 'webp'];
    const valid = files.filter(f => {
        const ext = f.name.split('.').pop().toLowerCase();
        return supported.includes(ext);
    });

    if (valid.length !== files.length) {
        showToast(`${files.length - valid.length} file ignorati (formato non supportato)`, 'error');
    }

    montageFiles.push(...valid);
    renderPhotoList();
    updateMontageButton();
}

function removePhoto(index) {
    montageFiles.splice(index, 1);
    renderPhotoList();
    updateMontageButton();
}

function renderPhotoList() {
    const list = document.getElementById('montagePhotoList');
    if (montageFiles.length === 0) {
        list.innerHTML = '';
        return;
    }

    list.innerHTML = montageFiles.map((f, i) => {
        const url = URL.createObjectURL(f);
        return `
            <div class="photo-thumb-wrap">
                <div class="photo-thumb" draggable="true" data-index="${i}">
                    <img src="${url}" alt="${f.name}">
                    <span class="photo-order">${i + 1}</span>
                    <button class="photo-remove" onclick="removePhoto(${i})">&times;</button>
                    <div class="photo-name">${f.name.length > 15 ? f.name.substring(0,12) + '...' : f.name}</div>
                </div>
                <input type="text" class="photo-caption-input" data-index="${i}" placeholder="didascalia..."
                       style="width:80px; font-size:0.6rem; padding:2px 4px; margin-top:2px; background:var(--bg-secondary); border:1px solid var(--border); color:var(--text-secondary); border-radius:4px;">
            </div>`;
    }).join('');

    // Drag & drop reorder
    const thumbs = list.querySelectorAll('.photo-thumb');
    thumbs.forEach(thumb => {
        thumb.addEventListener('dragstart', e => {
            e.dataTransfer.setData('text/plain', thumb.dataset.index);
            thumb.style.opacity = '0.5';
        });
        thumb.addEventListener('dragend', () => thumb.style.opacity = '1');
        thumb.addEventListener('dragover', e => e.preventDefault());
        thumb.addEventListener('drop', e => {
            e.preventDefault();
            const from = parseInt(e.dataTransfer.getData('text/plain'));
            const to = parseInt(thumb.dataset.index);
            if (from !== to) {
                const item = montageFiles.splice(from, 1)[0];
                montageFiles.splice(to, 0, item);
                renderPhotoList();
            }
        });
    });
}

// ─── Audio Upload ────────────────────────────────────
function addAudioFiles() {
    const input = document.getElementById('audioFileInput');
    const files = Array.from(input.files);
    const supported = ['mp3', 'wav', 'aac', 'ogg', 'flac', 'm4a'];

    files.forEach(f => {
        const ext = f.name.split('.').pop().toLowerCase();
        if (supported.includes(ext)) {
            montageAudioFiles.push(f);
        } else {
            showToast(`Audio ${f.name} non supportato`, 'error');
        }
    });

    renderAudioList();
    input.value = '';
}

function removeAudio(index) {
    montageAudioFiles.splice(index, 1);
    renderAudioList();
}

function renderAudioList() {
    const list = document.getElementById('audioList');
    if (montageAudioFiles.length === 0) {
        list.innerHTML = '<div style="color:var(--text-muted); font-size:0.8rem; text-align:center; padding:0.5rem;">Nessuna traccia audio aggiunta</div>';
        return;
    }

    list.innerHTML = montageAudioFiles.map((f, i) => `
        <div class="audio-item">
            <span class="audio-icon">&#9835;</span>
            <span class="audio-name">${f.name}</span>
            <span class="audio-size">${formatSize(f.size)}</span>
            <button class="photo-remove" onclick="removeAudio(${i})">&times;</button>
        </div>
    `).join('');
}

// ─── Style Selector ──────────────────────────────────
function initStyleSelector() {
    document.querySelectorAll('.style-card').forEach(el => {
        el.addEventListener('click', () => {
            document.querySelectorAll('.style-card').forEach(e => e.classList.remove('active'));
            el.classList.add('active');
            selectedStyle = parseInt(el.dataset.style);
        });
    });
}

// ─── Create Montage ──────────────────────────────────
async function createMontage() {
    if (montageFiles.length < 2) {
        showToast('Servono almeno 2 foto', 'error');
        return;
    }

    const btn = document.getElementById('btnMontage');
    btn.disabled = true;
    btn.textContent = 'Creazione in corso...';

    const form = new FormData();
    montageFiles.forEach(f => form.append('photos', f));
    montageAudioFiles.forEach(f => form.append('audio', f));

    form.append('name', document.getElementById('montageName').value || '');
    form.append('style', selectedStyle);
    form.append('outputWidth', document.getElementById('montageWidth').value);
    form.append('outputHeight', document.getElementById('montageHeight').value);
    form.append('fps', document.getElementById('montageFps').value);
    form.append('bitrateKbps', document.getElementById('montageBitrate').value);
    form.append('useHardwareAcceleration', document.getElementById('montageHwAccel').checked);
    form.append('photoDuration', document.getElementById('montagePhotoDuration').value);
    form.append('introText', document.getElementById('montageIntro').value);
    form.append('outroText', document.getElementById('montageOutro').value);
    form.append('audioFadeIn', document.getElementById('audioFadeIn').value);
    form.append('audioFadeOut', document.getElementById('audioFadeOut').value);

    // Cinematic settings
    form.append('colorGrade', document.getElementById('colorGrade').value);
    form.append('vignette', document.getElementById('vignette').checked);
    form.append('filmGrain', document.getElementById('filmGrain').checked);
    form.append('letterbox', document.getElementById('letterbox').checked);
    form.append('particleEffect', document.getElementById('particleEffect').value);
    form.append('memoryFlash', document.getElementById('memoryFlash').checked);

    // Intro cinematico
    const introTitle = document.getElementById('introTitle').value;
    if (introTitle) {
        form.append('introTitle', introTitle);
        form.append('introSubtitle', document.getElementById('introSubtitle').value);
        form.append('introDate', document.getElementById('introDate').value);
    }

    // Outro / dedica
    const outroTitle = document.getElementById('outroTitle').value;
    if (outroTitle) {
        form.append('outroTitle', outroTitle);
        form.append('outroMessage', document.getElementById('outroMessage').value);
        form.append('outroCredits', document.getElementById('outroCredits').value);
    }

    // Captions per foto
    const captionInputs = document.querySelectorAll('.photo-caption-input');
    if (captionInputs.length > 0) {
        const captions = [];
        captionInputs.forEach(input => {
            if (input.value.trim()) {
                captions.push({ index: parseInt(input.dataset.index), text: input.value.trim() });
            }
        });
        if (captions.length > 0) form.append('captions', JSON.stringify(captions));
    }

    try {
        const res = await fetch(`${API}/montage`, { method: 'POST', body: form });
        const data = await res.json();

        if (!res.ok) {
            showToast(data.error || 'Errore', 'error');
            return;
        }

        showToast(`Montaggio "${data.name}" avviato! ${data.totalPhotos} foto da animare`, 'success');
        startMontagePoll(data.id);

        // Reset
        montageFiles = [];
        montageAudioFiles = [];
        renderPhotoList();
        renderAudioList();
        updateMontageButton();
        loadMontages();
    } catch (err) {
        showToast('Errore di connessione', 'error');
    } finally {
        btn.disabled = false;
        btn.textContent = 'Crea Video';
    }
}

function updateMontageButton() {
    const btn = document.getElementById('btnMontage');
    const count = montageFiles.length;
    btn.disabled = count < 2;
    btn.textContent = count > 0 ? `Crea Video (${count} foto)` : 'Crea Video';
}

// ─── Polling ─────────────────────────────────────────
function startMontagePoll(id) {
    if (montagePollIntervals[id]) return;
    montagePollIntervals[id] = setInterval(async () => {
        try {
            const res = await fetch(`${API}/montage/${id}`);
            const m = await res.json();
            updateMontageCard(m);

            if (['Completed', 'Failed', 'Cancelled'].includes(m.status)) {
                clearInterval(montagePollIntervals[id]);
                delete montagePollIntervals[id];
                if (m.status === 'Completed') showToast(`"${m.name}" completato!`, 'success');
                if (m.status === 'Failed') showToast(`Errore: ${m.errorMessage}`, 'error');
            }
        } catch { }
    }, 1500);
}

// ─── Render Montages ─────────────────────────────────
async function loadMontages() {
    try {
        const res = await fetch(`${API}/montage`);
        const montages = await res.json();
        renderMontages(montages);
        montages.filter(m => !['Completed', 'Failed', 'Cancelled'].includes(m.status))
            .forEach(m => startMontagePoll(m.id));
    } catch { }
}

function renderMontages(montages) {
    const list = document.getElementById('montageList');
    if (!montages.length) {
        list.innerHTML = `
            <div class="empty-state">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M7 4v16M17 4v16M3 8h4m10 0h4M3 12h18M3 16h4m10 0h4M4 20h16a1 1 0 001-1V5a1 1 0 00-1-1H4a1 1 0 00-1 1v14a1 1 0 001 1z"/>
                </svg>
                <p>Nessun montaggio ancora</p>
                <p style="font-size:0.8rem; margin-top:0.5rem">Carica le foto, scegli uno stile e la musica!</p>
            </div>`;
        return;
    }

    list.innerHTML = montages.map(m => buildMontageCard(m)).join('');
}

function buildMontageCard(m) {
    const statusClass = m.status.toLowerCase();
    const isActive = !['Completed', 'Failed', 'Cancelled'].includes(m.status);
    const isCompleted = m.status === 'Completed';

    const phases = {
        'AnimatingPhotos': `Animazione foto ${m.photosAnimated}/${m.totalPhotos}`,
        'AssemblingVideo': 'Assemblaggio con transizioni',
        'MixingAudio': 'Mixaggio audio',
        'FinalEncoding': 'Encoding finale GPU'
    };

    const phaseText = phases[m.status] || m.statusMessage || m.status;
    const duration = m.totalDurationSeconds ? `${Math.floor(m.totalDurationSeconds / 60)}:${String(Math.floor(m.totalDurationSeconds % 60)).padStart(2, '0')}` : '';

    return `
        <div class="job-card" id="montage-${m.id}">
            <div class="job-header">
                <div class="job-name">
                    <span class="status-badge"><span class="status-dot ${statusClass}"></span></span>
                    ${m.name}
                </div>
                <span class="job-type">${m.style}</span>
            </div>
            <div class="job-status">
                ${phaseText}
                ${isActive ? ` - ${m.progress.toFixed(1)}%` : ''}
                ${isCompleted && duration ? ` | Durata: ${duration}` : ''}
            </div>
            <div class="progress-bar">
                <div class="progress-fill ${isCompleted ? 'completed' : ''} ${m.status === 'Failed' ? 'failed' : ''}"
                     style="width: ${m.progress}%"></div>
            </div>
            <div class="progress-text">
                <span>${m.totalPhotos} foto | ${m.audioTracks} tracce audio</span>
                <span>${isCompleted && m.fileSizeFormatted ? m.fileSizeFormatted : timeAgo(m.createdAt)}</span>
            </div>
            <div class="job-actions">
                ${isCompleted ? `
                    <button class="btn-sm btn-download" onclick="downloadMontage('${m.id}', '${m.name}')">Scarica Video</button>
                    <button class="btn-sm" onclick="previewMontage('${m.id}')">Anteprima</button>
                ` : ''}
                <button class="btn-sm btn-delete" onclick="deleteMontage('${m.id}')">
                    ${isActive ? 'Annulla' : 'Elimina'}
                </button>
            </div>
        </div>`;
}

function updateMontageCard(m) {
    const el = document.getElementById(`montage-${m.id}`);
    if (el) el.outerHTML = buildMontageCard(m);
    else loadMontages();
}

// ─── Actions ─────────────────────────────────────────
function downloadMontage(id, name) {
    const a = document.createElement('a');
    a.href = `${API}/montage/${id}/download`;
    a.download = `${name}.mp4`;
    a.click();
}

function previewMontage(id) {
    const modal = document.getElementById('videoModal');
    const video = document.getElementById('modalVideo');
    video.src = `${API}/montage/${id}/download`;
    modal.classList.add('show');
    video.play();
}

async function deleteMontage(id) {
    await fetch(`${API}/montage/${id}`, { method: 'DELETE' });
    if (montagePollIntervals[id]) { clearInterval(montagePollIntervals[id]); delete montagePollIntervals[id]; }
    loadMontages();
    showToast('Eliminato', 'info');
}

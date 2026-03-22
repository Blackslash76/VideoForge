const API = '/api';
let selectedFile = null;
let selectedAnimationType = 0;
let pollIntervals = {};

// ─── Init ────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    initUploadZone();
    initAnimTypes();
    initSliders();
    loadJobs();
    loadSystemInfo();
    setInterval(loadJobs, 3000);
});

// ─── Upload Zone ─────────────────────────────────────
function initUploadZone() {
    const zone = document.getElementById('uploadZone');
    const input = document.getElementById('fileInput');

    zone.addEventListener('dragover', e => { e.preventDefault(); zone.classList.add('dragover'); });
    zone.addEventListener('dragleave', () => zone.classList.remove('dragover'));
    zone.addEventListener('drop', e => {
        e.preventDefault();
        zone.classList.remove('dragover');
        if (e.dataTransfer.files.length) handleFile(e.dataTransfer.files[0]);
    });

    input.addEventListener('change', e => {
        if (e.target.files.length) handleFile(e.target.files[0]);
    });
}

function handleFile(file) {
    const ext = file.name.split('.').pop().toLowerCase();
    const supported = ['jpg', 'jpeg', 'png', 'bmp', 'gif', 'tiff', 'tif', 'webp'];
    if (!supported.includes(ext)) {
        showToast(`Formato .${ext} non supportato`, 'error');
        return;
    }

    selectedFile = file;
    const preview = document.getElementById('imagePreview');
    const img = document.getElementById('previewImg');
    const info = document.getElementById('imageInfo');

    const reader = new FileReader();
    reader.onload = e => {
        img.src = e.target.result;
        preview.style.display = 'block';

        const tempImg = new Image();
        tempImg.onload = () => {
            info.textContent = `${tempImg.width} x ${tempImg.height} px  |  ${formatSize(file.size)}  |  ${file.name}`;
        };
        tempImg.src = e.target.result;
    };
    reader.readAsDataURL(file);

    document.getElementById('uploadZone').style.display = 'none';
    document.getElementById('btnAnimate').disabled = false;
}

function removeImage() {
    selectedFile = null;
    document.getElementById('imagePreview').style.display = 'none';
    document.getElementById('uploadZone').style.display = 'block';
    document.getElementById('btnAnimate').disabled = true;
    document.getElementById('fileInput').value = '';
}

// ─── Animation Types ─────────────────────────────────
function initAnimTypes() {
    document.querySelectorAll('.anim-type').forEach(el => {
        el.addEventListener('click', () => {
            document.querySelectorAll('.anim-type').forEach(e => e.classList.remove('active'));
            el.classList.add('active');
            selectedAnimationType = parseInt(el.dataset.type);
            updateVisibleSettings();
        });
    });
    updateVisibleSettings();
}

function updateVisibleSettings() {
    document.querySelectorAll('.settings-kenburns, .settings-parallax, .settings-cinemagraph, .settings-ai')
        .forEach(el => el.style.display = 'none');

    switch (selectedAnimationType) {
        case 0: document.querySelectorAll('.settings-kenburns').forEach(el => el.style.display = ''); break;
        case 1: document.querySelectorAll('.settings-parallax').forEach(el => el.style.display = ''); break;
        case 2: document.querySelectorAll('.settings-cinemagraph').forEach(el => el.style.display = ''); break;
        case 3: document.querySelectorAll('.settings-ai').forEach(el => el.style.display = ''); break;
        case 4:
            document.querySelectorAll('.settings-kenburns, .settings-parallax').forEach(el => el.style.display = '');
            break;
    }
}

// ─── Sliders ─────────────────────────────────────────
function initSliders() {
    document.querySelectorAll('input[type="range"]').forEach(slider => {
        const output = document.getElementById(slider.id + 'Value');
        if (output) {
            output.textContent = slider.value;
            slider.addEventListener('input', () => output.textContent = slider.value);
        }
    });
}

// ─── Advanced Settings ───────────────────────────────
function toggleAdvanced() {
    document.getElementById('advancedSettings').classList.toggle('show');
}

// ─── Animate ─────────────────────────────────────────
async function animate() {
    if (!selectedFile) return;

    const btn = document.getElementById('btnAnimate');
    btn.disabled = true;
    btn.textContent = 'Elaborazione...';

    const form = new FormData();
    form.append('image', selectedFile);
    form.append('animationType', selectedAnimationType);
    form.append('durationSeconds', document.getElementById('duration').value);
    form.append('fps', document.getElementById('fps').value);
    form.append('outputWidth', document.getElementById('width').value);
    form.append('outputHeight', document.getElementById('height').value);
    form.append('bitrateKbps', document.getElementById('bitrate').value);
    form.append('useHardwareAcceleration', document.getElementById('hwAccel').checked);
    form.append('loop', document.getElementById('loop').checked);

    // Ken Burns
    form.append('kenBurnsDirection', document.getElementById('kbDirection').value);
    form.append('kenBurnsZoomFactor', document.getElementById('kbZoom').value);

    // Parallax
    form.append('parallaxIntensity', document.getElementById('parallaxIntensity').value);
    form.append('parallaxMotion', document.getElementById('parallaxMotion').value);

    // Cinemagraph
    form.append('cinemagraphMotionIntensity', document.getElementById('cineIntensity').value);
    form.append('cinemagraphMotionType', document.getElementById('cineMotion').value);

    // AI
    form.append('aiMotionStrength', document.getElementById('aiStrength').value);
    form.append('aiMotionSeed', document.getElementById('aiSeed').value);

    const maskInput = document.getElementById('maskInput');
    if (maskInput.files.length > 0) {
        form.append('cinemagraphMask', maskInput.files[0]);
    }

    try {
        const res = await fetch(`${API}/animation`, { method: 'POST', body: form });
        const data = await res.json();

        if (!res.ok) {
            showToast(data.error || 'Errore', 'error');
            return;
        }

        showToast(`Animazione ${data.animationType} avviata!`, 'success');
        startPolling(data.id);
        loadJobs();
        removeImage();
    } catch (err) {
        showToast('Errore di connessione', 'error');
    } finally {
        btn.disabled = false;
        btn.textContent = 'Anima Foto';
    }
}

// ─── Polling ─────────────────────────────────────────
function startPolling(jobId) {
    if (pollIntervals[jobId]) return;
    pollIntervals[jobId] = setInterval(async () => {
        try {
            const res = await fetch(`${API}/animation/${jobId}`);
            const job = await res.json();
            updateJobCard(job);

            if (['Completed', 'Failed', 'Cancelled'].includes(job.status)) {
                clearInterval(pollIntervals[jobId]);
                delete pollIntervals[jobId];
                if (job.status === 'Completed') showToast('Animazione completata!', 'success');
                if (job.status === 'Failed') showToast(`Errore: ${job.errorMessage}`, 'error');
            }
        } catch { }
    }, 1000);
}

// ─── Jobs ────────────────────────────────────────────
async function loadJobs() {
    try {
        const res = await fetch(`${API}/animation`);
        const jobs = await res.json();
        renderJobs(jobs);

        // Resume polling for active jobs
        jobs.filter(j => !['Completed', 'Failed', 'Cancelled'].includes(j.status))
            .forEach(j => startPolling(j.id));
    } catch { }
}

function renderJobs(jobs) {
    const list = document.getElementById('jobsList');

    if (!jobs.length) {
        list.innerHTML = `
            <div class="empty-state">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
                    <path d="M15 10l4.553-2.276A1 1 0 0121 8.618v6.764a1 1 0 01-1.447.894L15 14M5 18h8a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z"/>
                </svg>
                <p>Nessuna animazione ancora</p>
                <p style="font-size:0.8rem; margin-top:0.5rem">Carica una foto e scegli un effetto!</p>
            </div>`;
        return;
    }

    list.innerHTML = jobs.map(job => buildJobCard(job)).join('');
}

function buildJobCard(job) {
    const statusClass = job.status.toLowerCase();
    const isActive = !['Completed', 'Failed', 'Cancelled'].includes(job.status);
    const isCompleted = job.status === 'Completed';

    return `
        <div class="job-card" id="job-${job.id}" data-id="${job.id}">
            <div class="job-header">
                <div class="job-name">
                    <span class="status-badge">
                        <span class="status-dot ${statusClass}"></span>
                    </span>
                    ${job.sourceFileName}
                </div>
                <span class="job-type">${job.animationType}</span>
            </div>
            <div class="job-status">${job.statusMessage || job.status} ${isActive ? `- ${job.progress.toFixed(1)}%` : ''}</div>
            <div class="progress-bar">
                <div class="progress-fill ${isCompleted ? 'completed' : ''} ${job.status === 'Failed' ? 'failed' : ''}"
                     style="width: ${job.progress}%"></div>
            </div>
            <div class="progress-text">
                <span>${job.imageWidth}x${job.imageHeight}</span>
                <span>${isCompleted && job.fileSizeFormatted ? job.fileSizeFormatted : timeAgo(job.createdAt)}</span>
            </div>
            <div class="job-actions">
                ${isCompleted ? `
                    <button class="btn-sm btn-download" onclick="downloadVideo('${job.id}', '${job.sourceFileName}')">Scarica Video</button>
                    <button class="btn-sm" onclick="previewVideo('${job.id}')">Anteprima</button>
                    <button class="btn-sm" onclick="downloadDepthMap('${job.id}')">Depth Map</button>
                ` : ''}
                <button class="btn-sm btn-delete" onclick="deleteJob('${job.id}')">
                    ${isActive ? 'Annulla' : 'Elimina'}
                </button>
            </div>
        </div>`;
}

function updateJobCard(job) {
    const existing = document.getElementById(`job-${job.id}`);
    if (existing) {
        existing.outerHTML = buildJobCard(job);
    } else {
        loadJobs();
    }
}

// ─── Actions ─────────────────────────────────────────
async function downloadVideo(id, name) {
    const a = document.createElement('a');
    a.href = `${API}/animation/${id}/download`;
    a.download = `${name.split('.')[0]}_animated.mp4`;
    a.click();
}

async function downloadDepthMap(id) {
    const res = await fetch(`${API}/animation/${id}/depthmap`);
    if (res.ok) {
        const a = document.createElement('a');
        a.href = `${API}/animation/${id}/depthmap`;
        a.download = 'depthmap.png';
        a.click();
    } else {
        showToast('Depth map non disponibile', 'info');
    }
}

function previewVideo(id) {
    const modal = document.getElementById('videoModal');
    const video = document.getElementById('modalVideo');
    video.src = `${API}/animation/${id}/download`;
    modal.classList.add('show');
    video.play();
}

function closeModal() {
    const modal = document.getElementById('videoModal');
    const video = document.getElementById('modalVideo');
    video.pause();
    video.src = '';
    modal.classList.remove('show');
}

async function deleteJob(id) {
    try {
        await fetch(`${API}/animation/${id}`, { method: 'DELETE' });
        if (pollIntervals[id]) {
            clearInterval(pollIntervals[id]);
            delete pollIntervals[id];
        }
        loadJobs();
        showToast('Eliminato', 'info');
    } catch { }
}

// ─── System Info ─────────────────────────────────────
async function loadSystemInfo() {
    try {
        const res = await fetch(`${API}/video/system/info`);
        const info = await res.json();
        const bar = document.getElementById('systemBar');
        bar.innerHTML = `
            <div class="item"><span class="dot ${info.nvencAvailable ? 'green' : 'red'}"></span> NVENC: ${info.nvencAvailable ? 'OK' : 'N/A'}</div>
            <div class="item"><span class="dot green"></span> GPU: ${info.gpuName || 'N/A'}</div>
            <div class="item"><span class="dot green"></span> VRAM: ${info.memoryFree || 'N/A'} libera</div>
            <div class="item">${info.ffmpegVersion ? info.ffmpegVersion.substring(0, 40) : 'FFmpeg N/A'}</div>
        `;
    } catch {
        document.getElementById('systemBar').innerHTML = `
            <div class="item"><span class="dot yellow"></span> Sistema: info non disponibili</div>
        `;
    }
}

// ─── Toast ───────────────────────────────────────────
function showToast(message, type = 'info') {
    const container = document.getElementById('toasts');
    const toast = document.createElement('div');
    toast.className = `toast ${type}`;
    toast.textContent = message;
    container.appendChild(toast);
    setTimeout(() => toast.remove(), 4000);
}

// ─── Helpers ─────────────────────────────────────────
function formatSize(bytes) {
    const sizes = ['B', 'KB', 'MB', 'GB'];
    let i = 0;
    let b = bytes;
    while (b >= 1024 && i < sizes.length - 1) { b /= 1024; i++; }
    return `${b.toFixed(1)} ${sizes[i]}`;
}

function timeAgo(dateStr) {
    const diff = (Date.now() - new Date(dateStr).getTime()) / 1000;
    if (diff < 60) return 'adesso';
    if (diff < 3600) return `${Math.floor(diff/60)} min fa`;
    if (diff < 86400) return `${Math.floor(diff/3600)} ore fa`;
    return `${Math.floor(diff/86400)} giorni fa`;
}

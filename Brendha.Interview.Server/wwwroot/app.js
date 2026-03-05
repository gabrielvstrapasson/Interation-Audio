const HUB_URL = "/audioHub";

let connection;
let audioContext;
let processor;
let microphone;
let localStream;
let nextStartTime = 0;
let isMeetingActive = false;
window.isMeetingActive = false;
let interruptionFrames = 0;
let aiSpeakingTimeout;
let sessionTimerInterval;
let currentJobContext = null;
let currentResumeJson = null;
let aiTypingInterval;
let isPushingToTalk = false;
let currentResumeMode = 'upload';

let textQueue = "";
let isTyping = false;
let shouldClearSubtitle = true;

let mandatorySkills = [];
let niceToHaveSkills = [];
let benefits = [];

let activeSources = [];

const ui = {
    userCard: document.getElementById('userCard'),
    aiCard: document.getElementById('aiCard'),
    recDot: document.getElementById('recDot'),
    btnJoin: document.getElementById('btnJoin'),
    meetingControls: document.getElementById('meetingControls'),
    btnCam: document.getElementById('btnCam'),
    iconCam: document.getElementById('iconCam'),
    btnMic: document.getElementById('btnMic'),
    iconMic: document.getElementById('iconMic'),
    statusMicIcon: document.getElementById('statusMicIcon'),
    userVideo: document.getElementById('userVideo'),
    userPlaceholder: document.getElementById('userPlaceholder'),
    sessionTimer: document.getElementById('sessionTimer'),
    timeLeft: document.getElementById('timeLeft')
};

window.onload = async () => {
    await initMedia();
    await loadJobDetails();

    const uploadBtn = document.querySelector('button[onclick="openUploadModal()"]');
    if (uploadBtn) {
        uploadBtn.disabled = false;
        uploadBtn.classList.remove('disabled');
        uploadBtn.title = 'Enviar currículo';
    }

    if (ui.btnJoin) {
        ui.btnJoin.disabled = true;
        ui.btnJoin.style.opacity = '0.5';
        ui.btnJoin.style.cursor = 'not-allowed';
    }

    setInterval(() => {
        const now = new Date();
        document.getElementById('clock').innerText = now.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }, 1000);
};

window.addEventListener('beforeunload', () => { if (isMeetingActive) stopMeeting(); });

window.addEventListener('pageHide', () => {
    if (isMeetingActive) {
        stopMeeting();
    }
}, false);



async function loadJobDetails() {
    try {
        const response = await fetch('api/job');
        if (!response.ok) {
            console.error("Erro ao buscar vaga: " + response.status);
            return;
        }
        const job = await response.json();
        currentJobContext = job;

        document.getElementById('navTitle').innerText = job.title || job.Title || 'Vaga Técnica';
        document.getElementById('jobDescription').innerText = job.description || job.Description || 'Sem descrição';
        document.getElementById('jobSalary').innerText = job.salaryRange || job.SalaryRange || 'A combinar';

        const tagsContainer = document.getElementById('jobStackTags');
        const skills = job.mandatorySkills || job.MandatorySkills || [];
        if (tagsContainer && skills.length > 0) {
            tagsContainer.innerHTML = '';
            skills.slice(0, 3).forEach(skill => {
                const span = document.createElement('span');
                span.innerText = skill;
                tagsContainer.appendChild(span);
            });
        }

        const reqList = document.getElementById('jobRequirementsList');
        if (reqList) {
            reqList.innerHTML = '';
            if (skills.length > 0) {
                skills.forEach(skill => {
                    const li = document.createElement('li');
                    li.innerText = skill;
                    reqList.appendChild(li);
                });
            }
            const niceSkills = job.niceToHaveSkills || job.NiceToHaveSkills || [];
            if (niceSkills.length > 0) {
                niceSkills.forEach(skill => {
                    const li = document.createElement('li');
                    li.innerHTML = `${skill} <span style="color:#777">(Desejável)</span>`;
                    reqList.appendChild(li);
                });
            }
        }

        const benefitsList = document.getElementById('jobBenefitsList');
        const benefits = job.benefits || job.Benefits || [];
        if (benefitsList && benefits.length > 0) {
            benefitsList.innerHTML = '';
            benefits.forEach(benefit => {
                const li = document.createElement('li');
                li.innerText = benefit;
                benefitsList.appendChild(li);
            });
        }
    } catch (error) {
        console.error("Erro no loadJobDetails:", error);
    }
}

async function startMeeting() {

    if (!currentResumeJson) {
        alert("Por favor, envie seu currículo antes de iniciar a entrevista.");
        openUploadModal();
        return;
    }

    try {

        if (!localStream || !audioContext | audioContext.state === 'closed') {
            await initMedia();
        }
        if (audioContext && audioContext.state === 'suspended') {
            await audioContext.resume();
            console.log('AudioContext resumed for compatibility.');
        }

        document.getElementById('sessionLoading').classList.add('active');
        ui.btnJoin.disabled = true;

        connection = new signalR.HubConnectionBuilder()
            .withUrl(HUB_URL)
            .withAutomaticReconnect()
            .build();

        connection.on("ResumeRequired", () => { alert("Erro: Currículo não detectado no servidor."); stopMeeting(); });

        connection.on("ReceiveAudio", (base64String) => {
            playAudioChunk(base64String);
            toggleAISpeaking(true);
        });

        connection.on("ReceiveAIQuestion", (text) => {
            const box = document.getElementById('aiQuestionBox');
            const p = document.getElementById('aiQuestionText');
            if (box && p) {

                if (shouldClearSubtitle) {
                    p.innerText = "";
                    textQueue = "";
                    shouldClearSubtitle = false;
                    box.classList.add('visible');
                }

                const unescapedText = text.replace(/\\u([0-9a-fA-F]{4})/g, (m, p1) => String.fromCharCode(parseInt(p1, 16)));
                textQueue += unescapedText;

                box.style.borderColor = 'var(--accent)';
                setTimeout(() => box.style.borderColor = '', 300);

                if (!isTyping) {
                    processTextQueue(p);
                }
            }
        });

        connection.on("FeedbackGenerated", (feedbackObj) => {
            hideLoadingOverlay();

            const page = document.getElementById('feedbackPage');
            const content = document.getElementById('feedbackContent');

            if (connection) {
                connection.stop().then(() => console.log("Conexão Websocket encerrada com sucesso."))
                    .catch(err => console.warn("Erro ao fechar conexão:", err));
            }

            if (page && content && feedbackObj) {
                const adherence = feedbackObj.adherence ?? feedbackObj.Adherence ?? 0;
                const report = feedbackObj.report || feedbackObj.Report || "Nenhuma análise detalhada foi gerada.";
                const message = feedbackObj.message || feedbackObj.Message || "";

                const justification = feedbackObj.justification || feedbackObj.Justification || {};
                const expJust = justification.experience || justification.Experience || "Não avaliada.";
                const skillsJust = justification.skills || justification.Skills || "Não avaliada.";
                const overallJust = justification.overall || justification.Overall || "Não avaliada.";

                let adherenceClass = 'danger';
                if (adherence >= 70) adherenceClass = 'success';
                else if (adherence >= 40) adherenceClass = 'warning';

                const formatText = (txt) => txt.replace(/\n/g, '<br/>');

                let html = `
                    <div class="feedback-page-header">
                        <h3><i class="material-icons">insights</i> Feedback </h3>
                        <button class="btn-voltar" onclick="closeFeedbackPage()">
                            Concluir e voltar <i class="material-icons">logout</i>
                        </button>
                    </div>

                    <div class="feedback-dashboard">
                        
                        <div class="dashboard-sidebar">
                            <div class="feedback-card">
                                <div class="score-header">Aderência a Vaga</div>
                                <div class="score-circle ${adherenceClass}">
                                    <span>${adherence}%</span>
                                </div>
                                <div class="final-message-box">
                                    <strong>Conclusão</strong>
                                    <p>${formatText(message)}</p>
                                </div>
                            </div>
                        </div>

                        <div class="dashboard-main">
                            <div class="feedback-card">
                                
                                <div class="feedback-tabs-nav">
                                    <button class="fb-tab-btn active" onclick="switchFeedbackTab('fb-geral')">
                                        <i class="material-icons">dashboard</i> Visão Geral
                                    </button>
                                    <button class="fb-tab-btn" onclick="switchFeedbackTab('fb-skills')">
                                        <i class="material-icons">code</i> Habilidades
                                    </button>
                                    <button class="fb-tab-btn" onclick="switchFeedbackTab('fb-exp')">
                                        <i class="material-icons">work</i> Experiência
                                    </button>
                                </div>

                                <div id="fb-geral" class="fb-tab-content active custom-scroll">
                                    <div class="premium-card accent-glow">
                                        <h4><i class="material-icons">summarize</i> Resumo</h4>
                                        <p>${formatText(report)}</p>
                                    </div>
                                    
                                    <div class="premium-card">
                                        <h4><i class="material-icons">psychology</i> Justificativa Geral da Nota</h4>
                                        <p>${formatText(overallJust)}</p>
                                    </div>
                                </div>

                                <div id="fb-skills" class="fb-tab-content custom-scroll">
                                    <div class="premium-card accent-glow">
                                        <h4><i class="material-icons">integration_instructions</i> Análise de Habilidades</h4>
                                        <p>${formatText(skillsJust)}</p>
                                    </div>
                                </div>

                                <div id="fb-exp" class="fb-tab-content custom-scroll">
                                    <div class="premium-card accent-glow">
                                        <h4><i class="material-icons">business_center</i> Trilha Profissional & Experiência</h4>
                                        <p>${formatText(expJust)}</p>
                                    </div>
                                </div>

                            </div>
                        </div>
                    </div>
                `;
                content.innerHTML = html;
                page.classList.add('visible');
            }
        });

        connection.on("SessionStarted", (durationInSeconds) => {
            document.getElementById('sessionLoading').classList.remove('active');
            startSessionTimer(durationInSeconds);
            document.body.classList.remove('lobby-mode');
            document.body.classList.add('live-mode');
            document.querySelector('.live-badge').style.opacity = '1';
            ui.btnJoin.style.display = 'none';
            ui.meetingControls.style.display = 'flex';

            const uploadBtn = document.querySelector('button[onclick="openUploadModal()"]');
            if (uploadBtn) {
                uploadBtn.disabled = true;
                uploadBtn.classList.remove('disabled');
                uploadBtn.title = 'Não é possível alterar o curriculo durante a entrevista';
            }
        });

        connection.on("ReconnectionFailed", (message) => {
            console.error("Reconnection failed:", message);
            alert("A entrevista foi interrompida. Por favor, reinicie.");
        });

        connection.on("UpdateTimer", (remainingSeconds) => startSessionTimer(remainingSeconds));
        connection.on("InterviewEnded", () => stopMeeting());
        connection.on("ResumeRequired", () => {
            document.getElementById('sessionLoading').classList.remove('active');
            alert("Erro: O servidor não recebeu o currículo.");
            stopMeeting();
        });

        await connection.start();

        await connection.invoke("UploadResume", currentResumeJson);
        await connection.invoke("StartInterview");
        await connection.invoke("RequestSessionTimer");

        if (!isMicEnabled()) await initMedia();

        isMeetingActive = true;
        window.isMeetingActive = true;
        ui.recDot.style.backgroundColor = 'red';

    } catch (err) {
        console.error(err);
        alert("Erro ao conectar: " + err.message);
        stopMeeting();
    }
}

async function stopMeeting() {
    if (!isMeetingActive) return;
    isMeetingActive = false;
    window.isMeetingActive = false;

    console.log("Encerrando reunião e gerando feedback...");

    const loadingOverlay = document.getElementById('sessionLoading');
    if (loadingOverlay) {
        loadingOverlay.querySelector('h3').innerText = "Gerando Feedback...";
        loadingOverlay.querySelector('p').innerText = "A IA está analisando sua entrevista. Isso pode levar alguns segundos.";
        loadingOverlay.classList.add('active');
    }

    stopAudioOutput();

    stopAudioOutput();
    if (microphone) { try { microphone.disconnect(); } catch (e) { } microphone = null; }
    if (processor) { try { processor.disconnect(); } catch (e) { } processor = null; }
    if (localStream) { try { localStream.getTracks().forEach(track => track.stop()); } catch (e) { } localStream = null; }
    if (audioContext) { try { if (audioContext.state !== 'closed') await audioContext.close(); } catch (err) { } audioContext = null; }
    if (sessionTimerInterval) clearInterval(sessionTimerInterval);

    resetUI();

    if (connection) {
        try {
            if (connection.state === signalR.HubConnectionState.Connected) {
                await connection.invoke("EndInterview").catch(err => console.warn("Aviso ao backend falhou:", err));
            }
            await connection.stop();
        } catch (err) {
            console.warn("Erro ao parar conexão SignalR:", err);
            hideLoadingOverlay();
        }
    }
    else {
        hideLoadingOverlay();
    }

}

function hideLoadingOverlay() {
    const loadingOverlay = document.getElementById('sessionLoading');
    if (loadingOverlay) {
        loadingOverlay.classList.remove('active');
        setTimeout(() => {
            loadingOverlay.querySelector('h3').innerText = "Conectando com a Brendha...";
            loadingOverlay.querySelector('p').innerText = "Analisando seu perfil e o contexto da vaga.";
        }, 500);
    }
}

async function initMedia() {
    try {
        if (audioContext && audioContext.state !== 'closed') {
            await audioContext.close();
        }
        audioContext = new (window.AudioContext || window.webkitAudioContext)();

        localStream = await navigator.mediaDevices.getUserMedia({
            audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true },
            video: true
        });

        const audioTrack = localStream.getAudioTracks()[0];
        if (audioTrack) audioTrack.enabled = true;

        ui.iconMic.innerText = "mic_off";
        ui.btnMic.style.backgroundColor = "#ea4335";
        ui.statusMicIcon.style.display = 'inline-block';

        ui.userVideo.srcObject = localStream;
        ui.userVideo.style.display = 'block';
        ui.userPlaceholder.style.display = 'none';

        microphone = audioContext.createMediaStreamSource(localStream);

        await audioContext.audioWorklet.addModule('audio-processor.js');
        processor = new AudioWorkletNode(audioContext, 'audio-processor');

        processor.port.onmessage = (event) => {
            if (!isMeetingActive) return;

            const inputData = event.data;

            if (!isPushingToTalk) {
                inputData.fill(0);
            }
            const volume = getRootMeanSquare(inputData);
            const speechThreshold = 0.05;

            if (volume > speechThreshold) {
                interruptionFrames++;
            } else {
                interruptionFrames = 0;
            }

            if (interruptionFrames > 5 && ui.aiCard.classList.contains('speaking-ai')) {
                console.log("Interrupção detectada, parando a fala da IA.");
                stopAudioOutput();
                interruptionFrames = 0;
            }

            if (volume > 0.02) {
                ui.userCard.classList.add('speaking-user');
            } else {
                ui.userCard.classList.remove('speaking-user');
            }

            const pcmDataBase64 = convertFloat32ToInt16Base64(inputData);

            if (connection && connection.state === signalR.HubConnectionState.Connected) {
                connection.invoke("SendAudioChunk", pcmDataBase64).catch(err => console.error("Erro ao enviar áudio:", err));
            }
        };

        microphone.connect(processor);
        processor.connect(audioContext.destination);

    } catch (err) {
        console.error("Erro em initMedia:", err);
        alert(`Erro ao iniciar mídia: ${err.message}. Verifique as permissões de câmera e microfone.`);
    }
}

function toggleCamera() {
    if (!localStream) return;
    const videoTrack = localStream.getVideoTracks()[0];
    videoTrack.enabled = !videoTrack.enabled;

    ui.iconCam.innerText = videoTrack.enabled ? "videocam" : "videocam_off";
    ui.btnCam.style.backgroundColor = videoTrack.enabled ? "" : "#ea4335";
    ui.userVideo.style.display = videoTrack.enabled ? 'block' : 'none';
    ui.userPlaceholder.style.display = videoTrack.enabled ? 'none' : 'flex';
}

function startPTT() {
    if (!localStream || !isMeetingActive) return;

    isPushingToTalk = true;

    ui.iconMic.innerText = "mic";
    ui.btnMic.style.backgroundColor = "rgba(16, 185, 129, 0.8)";
    ui.statusMicIcon.style.display = 'none';
}

function stopPTT() {
    if (!localStream || !isMeetingActive) return;

    isPushingToTalk = false;

    ui.iconMic.innerText = "mic_off";
    ui.btnMic.style.backgroundColor = "#ea4335";
    ui.statusMicIcon.style.display = 'inline-block';
}

document.addEventListener('keydown', (e) => {

    if (e.code === 'Space' && !e.repeat && isMeetingActive) {
        e.preventDefault();
        startPTT();
    }
});

document.addEventListener('keyup', (e) => {
    if (e.code === 'Space' && isMeetingActive) {
        e.preventDefault();
        stopPTT();
    }
});

function isMicEnabled() {
    if (!localStream) return false;
    const tracks = localStream.getAudioTracks();
    return tracks.length > 0 && tracks[0].enabled;
}

function playAudioChunk(base64String) {
    if (!audioContext) return;

    const rawData = atob(base64String);
    const outputArray = new Uint8Array(rawData.length);
    for (let i = 0; i < rawData.length; ++i) {
        outputArray[i] = rawData.charCodeAt(i);
    }

    const dataView = new DataView(outputArray.buffer);
    const float32Data = new Float32Array(outputArray.length / 2);
    for (let i = 0; i < float32Data.length; i++) {
        float32Data[i] = dataView.getInt16(i * 2, true) / 32768;
    }

    const buffer = audioContext.createBuffer(1, float32Data.length, 24000);
    buffer.getChannelData(0).set(float32Data);

    const source = audioContext.createBufferSource();
    source.buffer = buffer;
    source.connect(audioContext.destination);

    const currentTime = audioContext.currentTime;
    const bufferDelay = 0.05;
    if (nextStartTime < currentTime) {
        nextStartTime = currentTime + bufferDelay;
    }

    source.start(nextStartTime);
    nextStartTime += buffer.duration;

    activeSources.push(source);

    source.onended = () => {
        activeSources = activeSources.filter(s => s !== source);
        if (activeSources.length === 0) {
            setTimeout(() => {
                toggleAISpeaking(false);

                shouldClearSubtitle = true;

            }, 100);
        }
    };
}

function stopAudioOutput() {
    activeSources.forEach(source => {
        try { source.stop(); } catch (e) { }
    });
    activeSources = [];
    if (audioContext) {
        nextStartTime = audioContext.currentTime;
    }
    toggleAISpeaking(false);
    shouldClearSubtitle = true;
}

function getRootMeanSquare(buffer) {
    let sum = 0;
    for (let i = 0; i < buffer.length; i++) {
        sum += buffer[i] * buffer[i];
    }
    return Math.sqrt(sum / buffer.length);
}

function toggleAISpeaking(isSpeaking) {
    clearTimeout(aiSpeakingTimeout);
    if (isSpeaking) {
        ui.aiCard.classList.add('speaking-ai');
        aiSpeakingTimeout = setTimeout(() => {
            if (activeSources.length === 0) ui.aiCard.classList.remove('speaking-ai');
        }, 500);
    } else {
        ui.aiCard.classList.remove('speaking-ai');
    }
}

function convertFloat32ToInt16Base64(float32Array) {
    const int16Array = new Int16Array(float32Array.length);
    for (let i = 0; i < float32Array.length; i++) {
        let s = Math.max(-1, Math.min(1, float32Array[i]));
        int16Array[i] = s < 0 ? s * 0x8000 : s * 0x7FFF;
    }
    let binary = '';
    const bytes = new Uint8Array(int16Array.buffer);
    for (let i = 0; i < bytes.byteLength; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return window.btoa(binary);
}

function startSessionTimer(seconds) {
    if (sessionTimerInterval) clearInterval(sessionTimerInterval);

    ui.sessionTimer.style.display = 'flex';
    let remaining = Math.floor(seconds);

    const updateDisplay = () => {
        if (remaining < 0) remaining = 0;
        const m = Math.floor(remaining / 60).toString().padStart(2, '0');
        const s = (remaining % 60).toString().padStart(2, '0');
        ui.timeLeft.innerText = `${m}:${s}`;
        ui.sessionTimer.classList.toggle('urgent', remaining < 60);
    };

    updateDisplay();

    sessionTimerInterval = setInterval(() => {
        remaining--;
        updateDisplay();
        if (remaining < 0) {
            clearInterval(sessionTimerInterval);
        }
    }, 1000);
}

const dropZone = document.getElementById('dropZone');
const fileInput = document.getElementById('resumeUpload');
const uploadText = document.getElementById('uploadText');
const currentResumeName = document.getElementById('currentResumeName');
const btnConfirm = document.getElementById('btnSendResume');

['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
    dropZone.addEventListener(eventName, (e) => {
        e.preventDefault();
        e.stopPropagation();
    }, false);
});
['dragenter', 'dragover'].forEach(eventName => {
    dropZone.addEventListener(eventName, () => dropZone.classList.add('highlight'), false);
});
['dragleave', 'drop'].forEach(eventName => {
    dropZone.addEventListener(eventName, () => dropZone.classList.remove('highlight'), false);
});

dropZone.addEventListener('drop', (e) => {
    fileInput.files = e.dataTransfer.files;
    handleFileSelect();
});

fileInput.addEventListener('change', handleFileSelect);

function switchResumeMode(mode) {
    currentResumeMode = mode;

    const uploadModeBtn = document.getElementById('uploadModeBtn');
    const textModeBtn = document.getElementById('textModeBtn');
    const uploadModeDiv = document.getElementById('uploadMode');
    const textModeDiv = document.getElementById('textMode');

    if (mode === 'upload') {
        uploadModeBtn.classList.add('active');
        textModeBtn.classList.remove('active');
        uploadModeDiv.classList.add('active');
        textModeDiv.classList.remove('active');
    } else {
        uploadModeBtn.classList.remove('active');
        textModeBtn.classList.add('active');
        uploadModeDiv.classList.remove('active');
        textModeDiv.classList.add('active');
    }
}

async function submitResume() {
    if (isMeetingActive) {
        alert("Não é possível trocar o currículo durante a entrevista.");
        return;
    }

    const btnSend = document.getElementById('btnSendResume');
    const nameEl = document.getElementById('currentResumeName');

    btnSend.innerText = 'Processando...';
    btnSend.disabled = true;

    try {
        if (currentResumeMode === 'upload') {
            const file = fileInput.files[0];
            if (!file) {
                alert("Selecione um arquivo PDF primeiro.");
                btnSend.disabled = false;
                btnSend.innerText = 'Enviar Currículo';
                return;
            }

            const formData = new FormData();
            formData.append('file', file);

            const response = await fetch('/api/resume', {
                method: 'POST',
                body: formData
            });

            if (!response.ok) throw new Error("Erro no processamento do servidor.");

            const data = await response.json();
            currentResumeJson = JSON.stringify(data);

            nameEl.innerText = file.name;

        } else {
            const textarea = document.getElementById('resumeTextArea');
            const resumeText = textarea.value.trim();

            if (!resumeText || resumeText.length < 50) {
                alert("Por favor, digite um currículo válido (mínimo 50 caracteres).");
                btnSend.disabled = false;
                btnSend.innerText = 'Enviar Currículo';
                return;
            }

            const response = await fetch('/api/resume/text', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ resumeText: resumeText })
            });

            if (!response.ok) throw new Error("Erro no processamento do servidor.");

            const data = await response.json();
            currentResumeJson = JSON.stringify(data);

            nameEl.innerText = 'Currículo Manual';
        }

        btnSend.innerText = 'Currículo Recebido ✓';
        btnSend.style.backgroundColor = 'var(--success)';

        if (ui.btnJoin) {
            ui.btnJoin.disabled = false;
            ui.btnJoin.style.opacity = '1';
            ui.btnJoin.style.cursor = 'pointer';
            ui.btnJoin.title = 'Iniciar Entrevista';
        }

        setTimeout(() => {
            closeUploadModal();
            resetUploadModal();
        }, 1500);

    } catch (error) {
        console.error(error);
        alert("Erro ao processar currículo. Tente novamente.");
        btnSend.innerText = 'Enviar Currículo';
        btnSend.disabled = false;
    }
}
async function handleFileSelect() {
    const file = fileInput.files[0];
    if (!file) return;

    if (isMeetingActive) {
        alert("Não é possível trocar o currículo durante a entrevista.");
        return;
    }

    uploadText.innerHTML = `<i class="material-icons" style="font-size:32px;color:var(--accent);margin-bottom:8px">description</i><br><strong>${file.name}</strong><span style="font-size:11px;color:var(--text-gray);display:block;margin-top:4px">${(file.size / 1024).toFixed(0)} KB</span>`;
}

const resumeTextArea = document.getElementById('resumeTextArea');
if (resumeTextArea) {
    resumeTextArea.addEventListener('input', (e) => {
        const count = e.target.value.length;
        document.getElementById('charCount').innerText = count.toLocaleString();
    });
}

function resetUploadModal(isError = false) {
    dropZone.style.pointerEvents = 'auto';
    const btnSend = document.getElementById('btnSendResume');
    btnSend.style.backgroundColor = '';
    btnSend.disabled = false;
    btnSend.innerText = 'Enviar Currículo';

    if (isError) {
        setTimeout(() => {
            uploadText.innerHTML = '<strong>Clique ou arraste o arquivo</strong><span>PDF até 10MB</span>';
            fileInput.value = '';
            const textarea = document.getElementById('resumeTextArea');
            if (textarea) textarea.value = '';
        }, 2000);
    }
}

setInterval(() => {
    const now = new Date();
    document.getElementById("clock").innerText = now.toLocaleTimeString(
        [],
        { hour: "2-digit", minute: "2-digit" },
    );
}, 1000);

let isPinned = false;
function toggleJobPanel() {
    document.getElementById("jobPanel").classList.toggle("open");
}
function togglePin() {
    const btn = document.querySelector(".pin-btn");
    isPinned = !isPinned;
    isPinned ? btn.classList.add("active") : btn.classList.remove("active");
}
function switchTab(tabId) {
    document
        .querySelectorAll(".tab-btn")
        .forEach((b) => b.classList.remove("active"));
    document
        .querySelectorAll(".tab-content")
        .forEach((c) => c.classList.remove("active"));
    document
        .querySelector(`button[onclick="switchTab('${tabId}')"]`)
        .classList.add("active");
    document.getElementById(tabId).classList.add("active");
}
function openUploadModal() {
    if (window.isMeetingActive) {
        alert('Não é possível enviar o currículo durante a entrevista.');
        return;
    }
    document.getElementById("uploadModal").classList.add("visible");
}

function closeUploadModal() {
    document.getElementById("uploadModal").classList.remove("visible");
}
document.addEventListener("click", (e) => {
    const panel = document.getElementById("jobPanel");
    const navBtn = document.querySelector('button[title="Ver Vaga"]');
    if (
        !panel.contains(e.target) &&
        !navBtn.contains(e.target) &&
        panel.classList.contains("open") &&
        !isPinned
    ) {
        panel.classList.remove("open");
    }
});

function resetUI() {

    ui.userVideo.srcObject = null;
    document.body.classList.remove('live-mode');
    document.body.classList.add('lobby-mode');
    document.querySelector('.live-badge').style.opacity = '0';

    ui.userCard.classList.remove('speaking-user');
    ui.aiCard.classList.remove('speaking-ai');

    ui.btnJoin.style.display = 'flex';

    ui.btnJoin.disabled = !currentResumeJson;
    ui.btnJoin.innerText = "Iniciar";

    ui.meetingControls.style.display = 'none';
    ui.userVideo.style.display = 'none';
    ui.userPlaceholder.style.display = 'flex';
    ui.recDot.style.backgroundColor = '';

    ui.iconCam.innerText = "videocam";
    ui.iconMic.innerText = "mic";
    ui.statusMicIcon.style.display = 'none';
    ui.btnCam.style.backgroundColor = "";
    ui.btnMic.style.backgroundColor = "";

    if (sessionTimerInterval) clearInterval(sessionTimerInterval);
    ui.sessionTimer.style.display = 'none';

    const aiBox = document.getElementById('aiQuestionBox');
    const aiText = document.getElementById('aiQuestionText');
    if (aiBox) aiBox.classList.remove('visible');
    if (aiText) setTimeout(() => aiText.innerText = '', 500);


    const uploadBtn = document.querySelector('button[onclick="openUploadModal()"]');
    if (uploadBtn) {
        uploadBtn.disabled = false;
        uploadBtn.classList.remove('disabled');
        uploadBtn.title = 'Enviar novo currículo';
    }
}


function setupTagsInput(inputId, displayId, tagsArray) {
    const input = document.getElementById(inputId);
    const display = document.getElementById(displayId);

    if (!input || !display) return;

    input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
            e.preventDefault();
            const value = input.value.trim();
            if (!value) return;
            if (!tagsArray.includes(value)) {
                tagsArray.push(value);
                renderTags(displayId, tagsArray);
            }
            input.value = '';
        }
    });

    display.addEventListener('click', (e) => {
        const btn = e.target.closest('.remove-tag');
        if (!btn) return;
        const tagEl = btn.closest('.tag-item');
        if (!tagEl) return;
        const value = tagEl.dataset.value;
        const idx = tagsArray.indexOf(value);
        if (idx >= 0) {
            tagsArray.splice(idx, 1);
            renderTags(displayId, tagsArray);
        }
    });
}
function renderTags(displayId, tagsArray) {
    const display = document.getElementById(displayId);
    if (!display) return;

    display.innerHTML = '';
    tagsArray.forEach((tag) => {
        const span = document.createElement('span');
        span.className = 'tag-item';
        span.dataset.value = tag;

        const textNode = document.createElement('span');
        textNode.className = 'tag-text';
        textNode.textContent = tag;
        span.appendChild(textNode);

        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'remove-tag';
        btn.title = 'Remover';
        btn.innerHTML = '<i class="material-icons">close</i>';
        span.appendChild(btn);

        display.appendChild(span);
    });
}

function removeTag(displayId, index) {
    let tagsArray;
    if (displayId === 'mandatoryTagsDisplay') tagsArray = mandatorySkills;
    else if (displayId === 'niceToHaveTagsDisplay') tagsArray = niceToHaveSkills;
    else if (displayId === 'benefitsTagsDisplay') tagsArray = benefits;

    if (tagsArray) {
        tagsArray.splice(index, 1);
        renderTags(displayId, tagsArray);
    }
}

function openEditJobModal() {
    if (isMeetingActive) {
        alert("Não é possível editar a vaga durante a entrevista.");
        return;
    }

    const modal = document.getElementById('editJobModal');

    if (currentJobContext) {
        document.getElementById('jobTitle').value = currentJobContext.title || currentJobContext.Title || '';
        document.getElementById('jobLevel').value = currentJobContext.level || currentJobContext.Level || '';
        document.getElementById('jobSalaryInput').value = currentJobContext.salaryRange || currentJobContext.SalaryRange || '';
        document.getElementById('jobDescriptionInput').value = currentJobContext.description || currentJobContext.Description || '';

        mandatorySkills = [...(currentJobContext.mandatorySkills || currentJobContext.MandatorySkills || [])];
        niceToHaveSkills = [...(currentJobContext.niceToHaveSkills || currentJobContext.NiceToHaveSkills || [])];
        benefits = [...(currentJobContext.benefits || currentJobContext.Benefits || [])];

        renderTags('mandatoryTagsDisplay', mandatorySkills);
        renderTags('niceToHaveTagsDisplay', niceToHaveSkills);
        renderTags('benefitsTagsDisplay', benefits);
    }

    modal.classList.add('visible');
}

function closeEditJobModal() {
    document.getElementById('editJobModal').classList.remove('visible');
}

async function saveJobChanges(e) {
    e.preventDefault();

    const btnSave = document.getElementById('btnSaveJob');
    const originalText = btnSave.innerHTML;

    btnSave.disabled = true;
    btnSave.innerHTML = '<i class="material-icons">hourglass_empty</i> Salvando...';

    try {
        const collectTagsFromDom = (displayId) => {
            const container = document.getElementById(displayId);
            if (!container) return [];
            return Array.from(container.querySelectorAll('.tag-item'))
                .map(el => el.dataset.value?.trim())
                .filter(Boolean);
        };

        const updatedJob = {
            title: document.getElementById('jobTitle').value.trim(),
            level: document.getElementById('jobLevel').value,
            description: document.getElementById('jobDescriptionInput').value.trim(),
            salaryRange: document.getElementById('jobSalaryInput').value.trim(),
            mandatorySkills: collectTagsFromDom('mandatoryTagsDisplay'),
            niceToHaveSkills: collectTagsFromDom('niceToHaveTagsDisplay'),
            benefits: collectTagsFromDom('benefitsTagsDisplay')
        };

        const response = await fetch('/api/job', {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(updatedJob)
        });

        if (!response.ok) throw new Error('Erro ao salvar alterações');

        const result = await response.json();

        currentJobContext = result.job;

        await loadJobDetails();

        btnSave.innerHTML = '<i class="material-icons">check</i> Salvo!';
        btnSave.style.backgroundColor = 'var(--success)';

        setTimeout(() => {
            closeEditJobModal();
            btnSave.innerHTML = originalText;
            btnSave.style.backgroundColor = '';
            btnSave.disabled = false;
        }, 1500);

    } catch (error) {
        console.error('Erro ao salvar vaga:', error);
        alert('Erro ao salvar alterações. Tente novamente.');
        btnSave.innerHTML = originalText;
        btnSave.disabled = false;
    }
}

async function resetJobToDefault() {
    if (!confirm('Tem certeza que deseja restaurar a vaga padrão? Esta ação não pode ser desfeita.')) {
        return;
    }

    const btnReset = document.querySelector('.btn-reset');
    const originalText = btnReset.innerHTML;

    btnReset.disabled = true;
    btnReset.innerHTML = '<i class="material-icons">hourglass_empty</i> Restaurando...';

    try {
        const response = await fetch('/api/job/reset', {
            method: 'POST'
        });

        if (!response.ok) throw new Error('Erro ao restaurar vaga padrão');

        const result = await response.json();
        currentJobContext = result.job;

        await loadJobDetails();

        closeEditJobModal();
        alert('Vaga restaurada para o padrão com sucesso!');

    } catch (error) {
        console.error('Erro ao restaurar vaga:', error);
        alert('Erro ao restaurar vaga padrão. Tente novamente.');
    } finally {
        btnReset.innerHTML = originalText;
        btnReset.disabled = false;
    }
}

function clearJobFields() {
    document.getElementById('jobTitle').value = '';
    document.getElementById('jobLevel').value = '';
    document.getElementById('jobSalaryInput').value = '';
    document.getElementById('jobDescriptionInput').value = '';

    mandatorySkills = [];
    niceToHaveSkills = [];
    benefits = [];

    renderTags('mandatoryTagsDisplay', mandatorySkills);
    renderTags('niceToHaveTagsDisplay', niceToHaveSkills);
    renderTags('benefitsTagsDisplay', benefits);
}

document.addEventListener('DOMContentLoaded', () => {
    const editForm = document.getElementById('editJobForm');
    if (editForm) {
        editForm.addEventListener('submit', saveJobChanges);
    }

    setupTagsInput('mandatorySkillsInput', 'mandatoryTagsDisplay', mandatorySkills);
    setupTagsInput('niceToHaveSkillsInput', 'niceToHaveTagsDisplay', niceToHaveSkills);
    setupTagsInput('benefitsInput', 'benefitsTagsDisplay', benefits);

    document.getElementById('editJobModal')?.addEventListener('click', (e) => {
        if (e.target.id === 'editJobModal') {
            closeEditJobModal();
        }
    });
});

function formatCurrency(value) {
    value = value.replace(/\D/g, '');
    const intValue = parseInt(value, 10);
    if (isNaN(intValue)) return '';
    return (intValue / 100).toLocaleString('pt-BR', {
        style: 'currency',
        currency: 'BRL',
    }).replace('R$', '').trim();
}

function updateSalaryField(initialInput, maxInput, finalField) {
    const initialValue = formatCurrency(initialInput.value);
    const maxValue = formatCurrency(maxInput.value);

    if (initialValue && maxValue) {
        finalField.value = `R$ ${initialValue} a R$ ${maxValue}`;
    } else if (initialValue) {
        finalField.value = `R$ ${initialValue}`;
    } else {
        finalField.value = '';
    }
}

function setupSalaryFields() {
    const initialInput = document.getElementById('valorInicial');
    const maxInput = document.getElementById('valorMaximo');
    const finalField = document.getElementById('jobSalaryInput');

    if (initialInput && maxInput && finalField) {
        initialInput.addEventListener('input', () => {
            initialInput.value = formatCurrency(initialInput.value);
            updateSalaryField(initialInput, maxInput, finalField);
        });

        maxInput.addEventListener('input', () => {
            maxInput.value = formatCurrency(maxInput.value);
            updateSalaryField(initialInput, maxInput, finalField);
        });
    }
}

document.addEventListener('DOMContentLoaded', () => {
    setupSalaryFields();
});

function processTextQueue(element) {
    if (textQueue.length > 0) {
        isTyping = true;
        element.innerText += textQueue.charAt(0);
        textQueue = textQueue.substring(1);

        setTimeout(() => processTextQueue(element), 50);
    } else {
        isTyping = false;
    }
}



function closeFeedbackPage() {
    const page = document.getElementById("feedbackPage");
    if (page) {
        page.classList.remove('visible');
    }
    resetUI();
}

function switchFeedbackTab(tabId) {
    document.querySelectorAll('.fb-tab-btn').forEach(btn => btn.classList.remove('active'));
    document.querySelectorAll('.fb-tab-content').forEach(content => content.classList.remove('active'));

    document.querySelector(`button[onclick="switchFeedbackTab('${tabId}')"]`).classList.add('active');
    document.getElementById(tabId).classList.add('active');
}
// ============================================
// 一级建造师模拟答题系统 - 前端应用
// ============================================

// State
const state = {
    categories: [],
    selectedCategories: [],  // multi-select
    questions: [],
    currentIndex: 0,
    answers: {},       // { questionId: answer }
    timerInterval: null,
    startTime: null,
    isLoading: false,
    countdownEnabled: false,
    countdownSeconds: 0,    // remaining seconds (countdown mode)
    autoSubmitted: false,
    resultId: null,         // server-side result ID for verification
    resultPollInterval: null
};

// API base URL
const API_BASE = '/api/exam';

// ============================================
// Initialization
// ============================================
document.addEventListener('DOMContentLoaded', async () => {
    await loadConfig();
    await loadCategories();

    // Setup type checkbox styling
    document.querySelectorAll('.checkbox-item').forEach(item => {
        const input = item.querySelector('input');
        input.addEventListener('change', () => {
            item.classList.toggle('has-checked', input.checked);
        });
        // Initial state
        if (input.checked) item.classList.add('has-checked');
    });
});

// ============================================
// Config Loading
// ============================================
async function loadConfig() {
    try {
        const resp = await fetch(API_BASE + '/config');
        if (!resp.ok) return;
        const config = await resp.json();
        if (config.title) {
            document.title = config.title;
            const h1 = document.querySelector('.logo h1');
            if (h1) h1.textContent = config.title;
        }
        if (config.subtitle) {
            const subEl = document.querySelector('.welcome-sub');
            if (subEl) subEl.textContent = config.subtitle;
        }
    } catch (e) { }

    // Check AI status for case analysis warning
    await checkAiStatus();
}

// ============================================
// Category Loading
// ============================================
async function loadCategories() {
    try {
        const resp = await fetch(`${API_BASE}/categories`);
        if (!resp.ok) throw new Error('加载失败');
        state.categories = await resp.json();
        renderCategories();
    } catch (err) {
        document.getElementById('categoryGrid').innerHTML =
            `<div class="loading-categories" style="color:var(--danger)">加载题库失败，请检查服务是否启动</div>`;
        console.error(err);
    }
}

function renderCategories() {
    const grid = document.getElementById('categoryGrid');

    if (state.categories.length === 0) {
        grid.innerHTML = '<div class="loading-categories">未找到题库，请检查题库目录</div>';
        return;
    }

    // Add "全选/取消" toggle
    grid.innerHTML = `
        <div class="category-toggle-all" onclick="toggleAllCategories()">
            <span id="toggleAllText">☐ 全选所有类别</span>
            <small id="toggleAllCount">${getTotalQuestions()} 题</small>
        </div>
    ` + state.categories.map(cat => `
        <div class="category-item ${state.selectedCategories.includes(cat.name) ? 'selected' : ''}"
             data-category="${escapeHtml(cat.name)}" onclick="toggleCategory('${escapeHtml(cat.name)}', this)">
            <div class="selected-indicator">✓</div>
            <div class="cat-name">${escapeHtml(cat.name)}</div>
            <div class="cat-count">${cat.totalQuestions} 题 · 单选${cat.singleChoiceCount} 多选${cat.multiChoiceCount} 判断${cat.trueFalseCount} 填空${cat.fillInBlankCount} 案例${cat.caseAnalysisCount}</div>
            ${cat.totalQuestions === 0 ? '<div class="cat-count" style="color:var(--danger)">⚠ 无可用题目</div>' : ''}
        </div>
    `).join('');
}

function getTotalQuestions() {
    return state.categories.reduce((sum, c) => sum + c.totalQuestions, 0);
}

function toggleAllCategories() {
    const valid = state.categories.filter(c => c.totalQuestions > 0).map(c => c.name);
    if (state.selectedCategories.length >= valid.length) {
        // Deselect all
        state.selectedCategories = [];
    } else {
        // Select all valid
        state.selectedCategories = [...valid];
    }
    document.getElementById('configError').style.display = 'none';
    rerenderCategoryItems();
}

function toggleCategory(name, el) {
    const idx = state.selectedCategories.indexOf(name);
    if (idx >= 0) {
        state.selectedCategories.splice(idx, 1);
    } else {
        state.selectedCategories.push(name);
    }
    document.getElementById('configError').style.display = 'none';
    rerenderCategoryItems();
}

function rerenderCategoryItems() {
    // Update selected states without full re-render
    document.querySelectorAll('.category-item').forEach(el => {
        const name = el.dataset.category;
        el.classList.toggle('selected', state.selectedCategories.includes(name));
    });
    // Update toggle all text
    const valid = state.categories.filter(c => c.totalQuestions > 0).length;
    const sel = state.selectedCategories.length;
    const toggleEl = document.getElementById('toggleAllText');
    const countEl = document.getElementById('toggleAllCount');
    if (sel >= valid) {
        toggleEl.textContent = '☑ 取消全选';
    } else if (sel > 0) {
        toggleEl.textContent = `☐ 已选 ${sel}/${valid} 个类别`;
    } else {
        toggleEl.textContent = '☐ 全选所有类别';
    }
    if (countEl) {
        const total = state.categories.filter(c => state.selectedCategories.includes(c.name))
            .reduce((sum, c) => sum + c.totalQuestions, 0);
        countEl.textContent = sel > 0 ? `已选 ${total} 题` : `${getTotalQuestions()} 题`;
    }
}

// ============================================
// Count Adjustment
// ============================================
function adjustCount(delta) {
    const input = document.getElementById('questionCount');
    let val = parseInt(input.value) || 20;
    val = Math.max(1, Math.min(100, val + delta));
    input.value = val;
}

// ============================================
// Countdown Toggle
// ============================================
function toggleCountdown() {
    const toggle = document.getElementById('countdownToggle');
    const select = document.getElementById('countdownMinutes');
    state.countdownEnabled = toggle.checked;
    select.disabled = !toggle.checked;
    updateCountdownLabel();
}

function updateCountdownLabel() {
    const toggle = document.getElementById('countdownToggle');
    const select = document.getElementById('countdownMinutes');
    const label = document.getElementById('countdownLabel');
    if (toggle.checked) {
        const min = select.value;
        label.textContent = '开启 · ' + min + ' 分钟';
    } else {
        label.textContent = '关闭';
    }
}

// ============================================
// Start Exam
// ============================================
async function startExam() {
    if (state.isLoading) return;

    // Validate
    if (state.selectedCategories.length === 0) {
        showConfigError('请至少选择一个题库类别');
        return;
    }

    const selectedTypes = getSelectedTypes();
    if (selectedTypes.length === 0) {
        showConfigError('请至少选择一种题型');
        return;
    }

    const count = parseInt(document.getElementById('questionCount').value) || 20;

    showLoading('正在准备题目...');

    try {
        const resp = await fetch(`${API_BASE}/questions`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                categories: state.selectedCategories,
                category: state.selectedCategories[0],  // backward compat
                types: selectedTypes,
                count: count
            })
        });

        if (!resp.ok) {
            const err = await resp.text();
            throw new Error(err || '获取题目失败');
        }

        const questions = await resp.json();

        if (questions.length === 0) {
            showConfigError('所选类别和题型没有可用题目，请调整选择');
            hideLoading();
            return;
        }

        state.questions = questions;
        state.currentIndex = 0;
        state.answers = Object.create(null);  // Clean object, no prototype
        state.startTime = Date.now();
        state.autoSubmitted = false;
        state.resultId = null;
        state.lastResult = null;
        stopResultPolling();

        // Read countdown settings
        state.countdownEnabled = document.getElementById('countdownToggle').checked;
        if (state.countdownEnabled) {
            state.countdownSeconds = parseInt(document.getElementById('countdownMinutes').value) * 60;
        }

        // Switch to exam page
        switchPage('pageExam');
        const catLabel = state.selectedCategories.length > 2
            ? state.selectedCategories.length + ' 个类别'
            : state.selectedCategories.join(' + ');
        document.getElementById('examCategory').textContent = catLabel;

        // Render first question
        renderCurrentQuestion();

        // Start timer
        startTimer();

        hideLoading();

    } catch (err) {
        showConfigError('获取题目失败: ' + err.message);
        hideLoading();
        console.error(err);
    }
}

// ============================================
// Exam Navigation
// ============================================
function renderCurrentQuestion() {
    const container = document.getElementById('questionsContainer');
    const q = state.questions[state.currentIndex];
    if (!q) return;

    const qNumber = state.currentIndex + 1;
    const total = state.questions.length;

    container.innerHTML = renderQuestionCard(q, qNumber);

    // Update navigation buttons
    updateNavButtons();

    // Update progress
    document.getElementById('examProgress').textContent = `${qNumber} / ${total}`;
    document.getElementById('progressBar').style.width = `${(qNumber / total) * 100}%`;

    // Scroll to top
    container.scrollIntoView({ behavior: 'smooth', block: 'start' });

    // Add event listeners for options
    attachOptionListeners(q);
}

function renderQuestionCard(q, qNumber) {
    const typeLabel = getTypeLabel(q.type);
    const typeBadge = getTypeBadge(q.type);

    let optionsHtml = '';
    if (q.type === 'case') {
        const savedAnswer = state.answers[q.id] || '';
        optionsHtml = `
            <textarea class="case-answer-area"
                      id="answer_${q.id}"
                      placeholder="请输入你的答案..."
                      onchange="saveCaseAnswer('${q.id}', this.value)"
                      oninput="autoSaveCaseAnswer('${q.id}', this.value)">${escapeHtml(savedAnswer)}</textarea>
        `;
    } else if (q.type === 'fill') {
        const blankCount = q.blankCount || 1;
        const savedAnswer = state.answers[q.id] || '';
        const savedParts = savedAnswer ? savedAnswer.split('|').map(s => s.trim()) : new Array(blankCount).fill('');
        
        // Split content by ______ markers and interleave input fields
        let contentHtml = q.contentHtml;
        let inputsHtml = '';
        for (let i = 0; i < blankCount; i++) {
            const idx = contentHtml.indexOf('______');
            if (idx >= 0) {
                inputsHtml += contentHtml.substring(0, idx);
                inputsHtml += `<span class="blank-input-wrapper"><input type="text" class="fill-blank-input"
                       id="blank_${q.id}_${i}"
                       placeholder="(${i + 1})"
                       value="${escapeHtml(savedParts[i] || '')}"
                       onchange="saveFillAnswer('${q.id}', collectFillAnswers('${q.id}', ${blankCount}))"
                       oninput="autoSaveFillAnswer('${q.id}', collectFillAnswers('${q.id}', ${blankCount}))"></span>`;
                contentHtml = contentHtml.substring(idx + 6); // skip '______'
            }
        }
        inputsHtml += contentHtml;
        
        optionsHtml = `<div class="fill-answer-area">${inputsHtml}</div>`;
    } else if (q.type === 'judge') {
        // Defensive: only treat valid judge answers as selected
        const rawAnswer = state.answers[q.id];
        const validJudge = ['对', '错'];
        const savedAnswer = validJudge.includes(rawAnswer) ? rawAnswer : '';
        optionsHtml = `<div class="options-list">` + validJudge.map((opt, idx) => `
            <div class="option-item ${savedAnswer === opt ? 'selected' : ''}"
                 data-question-id="${q.id}" data-value="${opt}"
                 data-index="${idx}">
                <span class="option-letter">${String.fromCharCode(65 + idx)}</span>
                <span class="option-text">${escapeHtml(opt)}</span>
            </div>
        `).join('') + `</div>`;
    } else {
        const savedAnswer = state.answers[q.id] || '';
        const isMulti = q.type === 'multi';
        const selectedAnswers = isMulti ? (savedAnswer || '').split('').filter(Boolean) : [savedAnswer];

        optionsHtml = `<div class="options-list">` + q.options.map((opt, idx) => {
            const letter = String.fromCharCode(65 + idx);
            const isSelected = isMulti ? selectedAnswers.includes(letter) : savedAnswer === letter;
            return `
                <div class="option-item ${isSelected ? 'selected' : ''}"
                     data-question-id="${q.id}" data-value="${letter}"
                     data-index="${idx}" data-multi="${isMulti}">
                    <span class="option-letter">${letter}</span>
                    <span class="option-text">${escapeHtml(opt)}</span>
                </div>
            `;
        }).join('') + `</div>`;
    }

    let difficultyHtml = '';
    if (q.difficulty) {
        const diffColor = q.difficulty === '基础' ? 'var(--success)' :
                          q.difficulty === '进阶' ? 'var(--warning)' : 'var(--danger)';
        difficultyHtml = `<span style="color:${diffColor};font-size:13px">难度: ${escapeHtml(q.difficulty)}</span>`;
    }

    return `
        <div class="question-card" id="qCard_${q.id}">
            <div class="question-header">
                <span class="question-badge ${typeBadge}">${typeLabel}</span>
                <div style="display:flex;align-items:center;gap:12px">
                    ${difficultyHtml}
                    <span class="question-number">第 ${qNumber} 题</span>
                </div>
            </div>
            <div class="question-body">
                <div class="question-content">${q.contentHtml}</div>
                ${optionsHtml}
            </div>
        </div>
    `;
}

function attachOptionListeners(q) {
    if (q.type === 'case' || q.type === 'fill') return;

    document.querySelectorAll(`[data-question-id="${q.id}"]`).forEach(el => {
        el.addEventListener('click', () => {
            const isMulti = el.dataset.multi === 'true';
            const value = el.dataset.value;
            const questionId = el.dataset.questionId;

            if (isMulti) {
                // Toggle multi-select
                let current = state.answers[questionId] || '';
                if (current.includes(value)) {
                    current = current.replace(value, '');
                } else {
                    current = (current + value).split('').sort().join('');
                }
                state.answers[questionId] = current;

                // Update UI
                document.querySelectorAll(`[data-question-id="${questionId}"]`).forEach(opt => {
                    const val = opt.dataset.value;
                    const selected = current.includes(val);
                    opt.classList.toggle('selected', selected);
                });
            } else {
                // Single select
                state.answers[questionId] = value;

                // Update UI
                document.querySelectorAll(`[data-question-id="${questionId}"]`).forEach(opt => {
                    opt.classList.toggle('selected', opt.dataset.value === value);
                });
            }
        });
    });
}

function saveCaseAnswer(id, value) {
    state.answers[id] = value;
}

function saveFillAnswer(id, value) {
    state.answers[id] = value;
}

let fillAnswerTimer = null;
function autoSaveFillAnswer(id, value) {
    clearTimeout(fillAnswerTimer);
    fillAnswerTimer = setTimeout(() => {
        state.answers[id] = value;
    }, 500);
}

function collectFillAnswers(id, blankCount) {
    const parts = [];
    for (let i = 0; i < blankCount; i++) {
        const el = document.getElementById("blank_" + id + "_" + i);
        parts.push(el ? el.value.trim() : "");
    }
    return parts.join(" | ");
}

let caseAnswerTimer = null;
function autoSaveCaseAnswer(id, value) {
    clearTimeout(caseAnswerTimer);
    caseAnswerTimer = setTimeout(() => {
        state.answers[id] = value;
    }, 300);
}

function updateNavButtons() {
    const prev = document.getElementById('btnPrev');
    const next = document.getElementById('btnNext');
    const submit = document.getElementById('btnSubmit');
    const total = state.questions.length;
    const current = state.currentIndex;

    prev.style.display = current > 0 ? 'inline-flex' : 'none';
    next.style.display = current < total - 1 ? 'inline-flex' : 'none';
    submit.style.display = current === total - 1 ? 'inline-flex' : 'none';
}

function prevQuestion() {
    if (state.currentIndex > 0) {
        state.currentIndex--;
        renderCurrentQuestion();
    }
}

function nextQuestion() {
    if (state.currentIndex < state.questions.length - 1) {
        state.currentIndex++;
        renderCurrentQuestion();
    }
}

// ============================================
// Submit & Grading
// ============================================
async function submitExam() {
    if (state.isLoading) return;

    // Warn about unanswered questions
    const unanswered = state.questions.filter(q => !state.answers[q.id] || state.answers[q.id].trim() === '').length;
    if (unanswered > 0) {
        if (!confirm(`还有 ${unanswered} 道题未作答，确定提交吗？`)) return;
    }

    stopTimer();
    showLoading('正在批改试卷...');

    try {
        const submission = {
            answers: state.questions.map(q => ({
                id: q.id,
                answer: state.answers[q.id] || ''
            }))
        };

        const resp = await fetch(`${API_BASE}/submit`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(submission)
        });

        if (!resp.ok) {
            const err = await resp.text();
            throw new Error(err || '提交失败');
        }

        const result = await resp.json();
        state.lastResult = result;
        state.resultId = result.resultId;

        renderResult(result);
        switchPage('pageResult');

        // Start server-side verification polling (every 5s)
        startResultPolling();

    } catch (err) {
        alert('提交失败: ' + err.message);
        console.error(err);
    } finally {
        hideLoading();
    }
}

// ============================================
// Direct Submit (skip to results)
// ============================================
function directSubmit() {
    if (state.isLoading) return;

    const total = state.questions.length;
    const answered = state.questions.filter(q => state.answers[q.id] && state.answers[q.id].trim() !== '').length;
    const unanswered = total - answered;

    let msg = '';
    if (unanswered > 0) {
        msg = '还有 ' + unanswered + ' 道题未作答（共 ' + total + ' 题），确定直接提交吗？\n\n未作答的题目将计为错误。';
    } else {
        msg = '确定提交答案吗？提交后不可修改。';
    }

    if (confirm(msg)) {
        submitExam();
    }
}

// ============================================
// Result Rendering
// ============================================
function renderResult(result) {
    state.lastResult = result;

    // Score rendered as Canvas image (tamper-proof)
    drawScoreCanvas(result.score);

    document.getElementById('resultTitle').textContent =
        result.score >= 90 ? '🏆 太棒了！' :
        result.score >= 70 ? '👍 表现不错！' :
        result.score >= 60 ? '📚 还需努力' :
        '💪 继续加油！';

    // Summary
    let timeStr;
    if (state.countdownEnabled) {
        const total = parseInt(document.getElementById('countdownMinutes').value) * 60;
        const used = total - state.countdownSeconds;
        timeStr = formatTime(Math.abs(used));
        if (state.autoSubmitted) {
            timeStr = formatTime(total) + ' (时间用完)';
        }
    } else {
        timeStr = state.startTime ? formatTime((Date.now() - state.startTime) / 1000) : '--';
    }
    document.getElementById('resultSummary').textContent =
        '共 ' + result.totalQuestions + ' 题，答对 ' + result.correctCount + ' 题 · 用时 ' + timeStr;

    // Stats cards
    const statsHtml = '' +
        '<div class="stat-card">' +
            '<div class="stat-value purple">' + result.totalQuestions + '</div>' +
            '<div class="stat-label">总题数</div>' +
        '</div>' +
        '<div class="stat-card">' +
            '<div class="stat-value green">' + result.correctCount + '</div>' +
            '<div class="stat-label">正确</div>' +
        '</div>' +
        '<div class="stat-card">' +
            '<div class="stat-value red">' + (result.totalQuestions - result.correctCount) + '</div>' +
            '<div class="stat-label">错误</div>' +
        '</div>' +
        '<div class="stat-card">' +
            '<div class="stat-value" style="color:var(--primary)">' + result.score + '%</div>' +
            '<div class="stat-label">得分率</div>' +
        '</div>';
    document.getElementById('resultStats').innerHTML = statsHtml;

    // Detail
    const detailHtml = result.questions.map(function(q, idx) { return renderResultQuestion(q, idx + 1); }).join('');
    document.getElementById('resultDetail').innerHTML = detailHtml;
}

// ============================================
// Canvas Score Rendering (tamper-proof)
// ============================================
function drawScoreCanvas(score) {
    const container = document.getElementById('scoreCircle');
    // Replace score-circle content with a canvas
    container.innerHTML = '';

    const canvas = document.createElement('canvas');
    const size = 130;
    const dpr = window.devicePixelRatio || 1;
    canvas.width = size * dpr;
    canvas.height = size * dpr;
    canvas.style.width = size + 'px';
    canvas.style.height = size + 'px';

    const ctx = canvas.getContext('2d');
    ctx.scale(dpr, dpr);

    const cx = size / 2;
    const cy = size / 2;
    const radius = 54;

    // Gradient background circle
    const grad = ctx.createLinearGradient(cx - radius, cy - radius, cx + radius, cy + radius);
    grad.addColorStop(0, '#4f46e5');
    grad.addColorStop(1, '#7c3aed');
    ctx.beginPath();
    ctx.arc(cx, cy, radius, 0, Math.PI * 2);
    ctx.fillStyle = grad;
    ctx.fill();

    // Shadow
    ctx.shadowColor = 'rgba(79, 70, 229, 0.4)';
    ctx.shadowBlur = 15;
    ctx.fill();
    ctx.shadowColor = 'transparent';
    ctx.shadowBlur = 0;

    // Score number
    ctx.fillStyle = '#ffffff';
    ctx.font = 'bold 42px Inter, Noto Sans SC, sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(String(score), cx, cy - 4);

    // Unit text
    ctx.font = '500 14px Inter, Noto Sans SC, sans-serif';
    ctx.fillStyle = 'rgba(255,255,255,0.8)';
    ctx.fillText('分', cx, cy + 24);

    container.appendChild(canvas);
}

// ============================================
// Server-side Result Polling (every 5s)
// ============================================
function startResultPolling() {
    stopResultPolling();
    if (!state.resultId) return;

    state.resultPollInterval = setInterval(async function() {
        try {
            const resp = await fetch(API_BASE + '/result/' + state.resultId);
            if (!resp.ok) return;
            const serverResult = await resp.json();

            // If server result differs from local, update the display
            if (serverResult.score !== state.lastResult.score ||
                serverResult.correctCount !== state.lastResult.correctCount) {
                console.log('Score updated from server:', serverResult.score);
                drawScoreCanvas(serverResult.score);
                document.getElementById('resultSummary').textContent =
                    '共 ' + serverResult.totalQuestions + ' 题，答对 ' + serverResult.correctCount + ' 题';
                state.lastResult = serverResult;
            }
        } catch (e) {
            // Silent fail on network errors
        }
    }, 5000);
}

function stopResultPolling() {
    if (state.resultPollInterval) {
        clearInterval(state.resultPollInterval);
        state.resultPollInterval = null;
    }
}

function renderResultQuestion(q, number) {
    const typeLabel = getTypeLabel(q.type);
    const icon = q.isCorrect ? '✅' : '❌';
    const headerClass = q.isCorrect ? 'correct' : 'wrong';

    let answerSection = '';
    if (q.type === 'case') {
        // AI-scored question
        answerSection = `
            <div class="result-answer-row">
                <div class="result-answer-item user">
                    <div class="label">你的答案</div>
                    <div class="value" style="white-space:pre-wrap">${escapeHtml(q.userAnswer || '(未作答)')}</div>
                </div>
                <div class="result-answer-item correct-ans">
                    <div class="label">参考答案</div>
                    <div class="value" style="white-space:pre-wrap">${escapeHtml(q.correctAnswer)}</div>
                </div>
            </div>
            ${q.aiScoreDetail ? `
            <div class="ai-score-box">
                <strong>🤖 AI 评分详情</strong>
                <div style="white-space:pre-wrap">${escapeHtml(q.aiScoreDetail)}</div>
            </div>` : ''}
        `;
    } else {
        // Objective question
        answerSection = `
            <div class="result-answer-row">
                <div class="result-answer-item user">
                    <div class="label">你的答案</div>
                    <div class="value">${escapeHtml(q.userAnswer || '未作答')}</div>
                </div>
                <div class="result-answer-item correct-ans">
                    <div class="label">正确答案</div>
                    <div class="value">${escapeHtml(q.correctAnswer)}</div>
                </div>
            </div>
        `;
    }

    // Analysis
    let analysisSection = '';
    if (q.analysisHtml && q.analysisHtml.trim()) {
        analysisSection = `
            <div class="analysis-box">
                <strong>💡 题目解析</strong>
                <div>${q.analysisHtml}</div>
            </div>
        `;
    } else if (!q.isCorrect && q.type !== 'case') {
        // For wrong answers without explicit analysis, show the correct answer prominently
    }

    return `
        <div class="result-question">
            <div class="result-question-header ${headerClass}">
                <span>
                    <span class="result-icon">${icon}</span>
                    <span style="font-weight:600;margin-left:8px">第 ${number} 题</span>
                    <span class="question-badge ${getTypeBadge(q.type)}" style="margin-left:12px">${typeLabel}</span>
                </span>
            </div>
            <div class="result-question-body">
                <div class="question-content">${q.contentHtml}</div>
                ${q.options && q.options.length > 0 ? `
                    <div class="options-list" style="pointer-events:none;opacity:0.85">
                        ${q.options.map((opt, idx) => {
                            const letter = String.fromCharCode(65 + idx);
                            const isCorrectOption = q.correctAnswer.includes(letter);
                            const isUserSelected = (q.userAnswer || '').includes(letter);
                            let cls = '';
                            if (isCorrectOption && isUserSelected) cls = 'selected';
                            else if (isCorrectOption) cls = 'selected';
                            else if (isUserSelected && !isCorrectOption) cls = '';
                            return `
                                <div class="option-item ${cls}" style="
                                    ${isCorrectOption ? 'border-color:var(--success);background:var(--success-bg)' : ''}
                                    ${isUserSelected && !isCorrectOption ? 'border-color:var(--danger);background:var(--danger-bg)' : ''}
                                ">
                                    <span class="option-letter" style="
                                        ${isCorrectOption ? 'background:var(--success);color:white' : ''}
                                        ${isUserSelected && !isCorrectOption ? 'background:var(--danger);color:white' : ''}
                                    ">${letter}</span>
                                    <span class="option-text">${escapeHtml(opt)}</span>
                                </div>
                            `;
                        }).join('')}
                    </div>
                ` : ''}
                ${answerSection}
                ${analysisSection}
            </div>
        </div>
    `;
}

// ============================================
// Timer (supports elapsed + countdown modes)
// ============================================
function startTimer() {
    stopTimer();
    const timerEl = document.getElementById('examTimer');
    const iconEl = document.getElementById('timerIcon');
    timerEl.classList.remove('warning', 'danger');

    if (state.countdownEnabled) {
        iconEl.textContent = '⏳';
        state.startTime = null;
        updateCountdownDisplay();
        state.timerInterval = setInterval(updateCountdownDisplay, 1000);
    } else {
        iconEl.textContent = '⏱';
        state.startTime = Date.now();
        state.countdownSeconds = 0;
        updateElapsedDisplay();
        state.timerInterval = setInterval(updateElapsedDisplay, 1000);
    }
}

function stopTimer() {
    if (state.timerInterval) {
        clearInterval(state.timerInterval);
        state.timerInterval = null;
    }
}

function updateElapsedDisplay() {
    if (!state.startTime) return;
    const elapsed = Math.floor((Date.now() - state.startTime) / 1000);
    document.getElementById('timerDisplay').textContent = formatTime(elapsed);
}

function updateCountdownDisplay() {
    const timerEl = document.getElementById('examTimer');
    const display = document.getElementById('timerDisplay');

    state.countdownSeconds--;
    if (state.countdownSeconds <= 0) {
        state.countdownSeconds = 0;
        display.textContent = '00:00';
        stopTimer();
        if (!state.autoSubmitted) {
            state.autoSubmitted = true;
            showLoading('时间到，正在自动提交...');
            submitExam();
        }
        return;
    }

    display.textContent = formatTime(state.countdownSeconds);

    // Visual warnings
    timerEl.classList.remove('warning', 'danger');
    if (state.countdownSeconds <= 60) {
        timerEl.classList.add('danger');
    } else if (state.countdownSeconds <= 300) {
        timerEl.classList.add('warning');
    }
}

function formatTime(seconds) {
    const m = Math.floor(seconds / 60);
    const s = Math.floor(seconds % 60);
    return String(m).padStart(2, '0') + ':' + String(s).padStart(2, '0');
}

// ============================================
// Navigation
// ============================================
function switchPage(pageId) {
    document.querySelectorAll('.page').forEach(p => p.classList.remove('active'));
    document.getElementById(pageId).classList.add('active');
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function backToConfig() {
    state.questions = [];
    state.currentIndex = 0;
    state.answers = Object.create(null);
    state.countdownEnabled = false;
    state.countdownSeconds = 0;
    state.autoSubmitted = false;
    state.resultId = null;
    state.lastResult = null;
    stopTimer();
    stopResultPolling();
    // Reset timer display style
    const timerEl = document.getElementById('examTimer');
    timerEl.classList.remove('warning', 'danger');
    document.getElementById('timerIcon').textContent = '⏱';
    switchPage('pageConfig');
    // Reload categories in case bank changed
    loadCategories();
}

// ============================================
// UI Helpers
// ============================================
function showLoading(text) {
    state.isLoading = true;
    document.getElementById('loadingText').textContent = text || '加载中...';
    document.getElementById('loadingOverlay').style.display = 'flex';
}

function hideLoading() {
    state.isLoading = false;
    document.getElementById('loadingOverlay').style.display = 'none';
}

function showConfigError(msg) {
    const el = document.getElementById('configError');
    el.textContent = msg;
    el.style.display = 'block';
    setTimeout(() => { el.style.display = 'none'; }, 4000);
}

function getSelectedTypes() {
    const types = [];
    document.querySelectorAll('.checkbox-item input:checked').forEach(cb => {
        types.push(cb.value);
    });
    return types;
}

function getTypeLabel(type) {
    const map = {
        'single': '单选题',
        'multi': '多选题',
        'judge': '判断题',
        'case': '案例分析/简答',
        'fill': '填空题'
    };
    return map[type] || type;
}

function getTypeBadge(type) {
    const map = {
        'single': 'badge-single',
        'multi': 'badge-multi',
        'judge': 'badge-judge',
        'case': 'badge-case',
        'fill': 'badge-fill'
    };
    return map[type] || '';
}

function escapeHtml(text) {
    if (!text) return '';
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

// ============================================
// Settings Management
// ============================================
async function checkAiStatus() {
    try {
        const resp = await fetch(API_BASE + '/settings/ai-status');
        if (!resp.ok) return;
        const data = await resp.json();
        const caseCheckbox = document.querySelector('.checkbox-item input[value="case"]');
        const warning = document.getElementById('aiWarning');
        if (!data.configured) {
            if (caseCheckbox) {
                caseCheckbox.checked = false;
                caseCheckbox.parentElement.classList.remove('has-checked');
                caseCheckbox.disabled = true;
                caseCheckbox.parentElement.style.opacity = '0.5';
            }
            if (warning) warning.style.display = 'block';
        } else {
            if (caseCheckbox) caseCheckbox.disabled = false;
            if (warning) warning.style.display = 'none';
        }
    } catch (e) { }
}

async function openSettings() {
    try {
        const resp = await fetch(API_BASE + '/settings');
        const data = await resp.json();
        document.getElementById('settingApiUrl').value = data.apiUrl || '';
        document.getElementById('settingApiKey').value = data.apiKeyMasked || '';
        document.getElementById('settingsStatus').className = 'modal-status';
        document.getElementById('settingsStatus').style.display = 'none';
    } catch (e) {
        document.getElementById('settingApiUrl').value = '';
        document.getElementById('settingApiKey').value = '';
    }
    document.getElementById('settingsModal').style.display = 'flex';
}

function closeSettings() {
    document.getElementById('settingsModal').style.display = 'none';
}

async function saveSettings() {
    const status = document.getElementById('settingsStatus');
    const apiKey = document.getElementById('settingApiKey').value.trim();
    const apiUrl = document.getElementById('settingApiUrl').value.trim();

    try {
        const resp = await fetch(API_BASE + '/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ apiUrl: apiUrl, apiKey: apiKey })
        });
        const data = await resp.json();
        if (data.success) {
            status.className = 'modal-status success';
            status.style.display = 'block';
            status.textContent = '✅ 设置已保存';
            await checkAiStatus();
            setTimeout(closeSettings, 1500);
        }
    } catch (e) {
        status.className = 'modal-status error';
        status.style.display = 'block';
        status.textContent = '❌ 保存失败: ' + e.message;
    }
}

async function verifySettings() {
    const status = document.getElementById('settingsStatus');
    status.className = 'modal-status';
    status.style.display = 'block';
    status.textContent = '⏳ 正在测试连接...';

    // Save first, then verify
    const apiKey = document.getElementById('settingApiKey').value.trim();
    const apiUrl = document.getElementById('settingApiUrl').value.trim();

    if (apiKey && apiKey.length > 10 && !apiKey.includes('***')) {
        await fetch(API_BASE + '/settings', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ apiUrl: apiUrl, apiKey: apiKey })
        });
    }

    try {
        const resp = await fetch(API_BASE + '/settings/verify', { method: 'POST' });
        const data = await resp.json();
        if (data.ok) {
            status.className = 'modal-status success';
            status.textContent = '✅ ' + data.message;
            await checkAiStatus();
        } else {
            status.className = 'modal-status error';
            status.textContent = '❌ ' + data.message;
        }
    } catch (e) {
        status.className = 'modal-status error';
        status.textContent = '❌ 测试失败: ' + e.message;
    }
}

// Close modal on overlay click
document.addEventListener('click', function(e) {
    if (e.target.id === 'settingsModal') {
        closeSettings();
    }
});

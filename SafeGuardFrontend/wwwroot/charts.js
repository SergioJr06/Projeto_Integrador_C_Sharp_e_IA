/*
 * Gráficos do painel em SVG puro: não dependem de CDN nem de biblioteca externa.
 * Os dados vêm do banco de dados (via servidor) nos atributos data-cats e data-days.
 *   #epiChart      data-cats='[{"name":"Capacete","count":10}, ...]'
 *   #timelineChart data-days='[{"label":"24/10","count":3}, ...]'
 */
(function () {
  'use strict';
  var NS = 'http://www.w3.org/2000/svg';
  var COR = '#1f3a5f', COR_CLARA = 'rgba(31,58,95,.14)', GRADE = '#e5e7eb', TEXTO = '#4b5563', EIXO = '#9ca3af';

  function el(tag, attrs, parent) {
    var n = document.createElementNS(NS, tag);
    for (var k in attrs) if (Object.prototype.hasOwnProperty.call(attrs, k)) n.setAttribute(k, attrs[k]);
    if (parent) parent.appendChild(n);
    return n;
  }
  function texto(parent, x, y, conteudo, extra) {
    var attrs = { x: x, y: y, fill: TEXTO, 'font-size': 11 };
    for (var k in extra) attrs[k] = extra[k];
    var t = el('text', attrs, parent);
    t.textContent = conteudo;
    return t;
  }
  function vazio(container, msg) {
    container.innerHTML = '';
    var p = document.createElement('p');
    p.className = 'chart-vazio';
    p.textContent = msg;
    container.appendChild(p);
  }
  function plural(n) { return n + (n === 1 ? ' ocorrência' : ' ocorrências'); }

  // Valores "redondos" para o eixo: 0, passo, 2*passo... (sempre inteiros, no máx. ~5 marcas)
  function marcas(max) {
    var passo = Math.max(1, Math.ceil(max / 4));
    var lim = Math.ceil(max / passo) * passo;
    var lista = [];
    for (var v = 0; v <= lim; v += passo) lista.push(v);
    return lista;
  }

  function dica(container) {
    var d = container.querySelector('.chart-tip');
    if (!d) {
      d = document.createElement('div');
      d.className = 'chart-tip';
      container.appendChild(d);
    }
    d.style.display = 'none';
    return {
      mostrar: function (x, y, html) {
        d.innerHTML = html;
        d.style.display = 'block';
        var w = d.offsetWidth, cw = container.clientWidth;
        d.style.left = Math.max(0, Math.min(cw - w, x - w / 2)) + 'px';
        d.style.top = Math.max(0, y - d.offsetHeight - 10) + 'px';
      },
      esconder: function () { d.style.display = 'none'; }
    };
  }

  function barras(container, dados) {
    container.innerHTML = '';
    if (!dados.length) return vazio(container, 'Sem infrações no período selecionado.');
    var total = dados.reduce(function (s, d) { return s + d.count; }, 0);
    var max = Math.max.apply(null, dados.map(function (d) { return d.count; }));
    var ticks = marcas(max), topo = ticks[ticks.length - 1];
    var largura = Math.max(container.clientWidth, 260);
    var maiorNome = Math.max.apply(null, dados.map(function (d) { return d.name.length; }));
    var ml = Math.min(170, Math.max(80, maiorNome * 6.6 + 14)), mr = 38, mt = 6, mb = 26;
    var linha = Math.max(28, Math.min(44, Math.floor((container.clientHeight - mt - mb) / dados.length)));
    var altura = mt + mb + linha * dados.length;
    var area = largura - ml - mr;

    var svg = el('svg', { width: largura, height: altura, viewBox: '0 0 ' + largura + ' ' + altura, role: 'img',
      'aria-label': 'Infrações por tipo de EPI: ' + dados.map(function (d) { return d.name + ' ' + d.count; }).join(', ') }, container);
    ticks.forEach(function (v) {
      var x = ml + (v / topo) * area;
      el('line', { x1: x, x2: x, y1: mt, y2: altura - mb, stroke: GRADE, 'stroke-dasharray': '3 3' }, svg);
      texto(svg, x, altura - 8, v, { 'text-anchor': 'middle' });
    });
    var tip = dica(container);
    dados.forEach(function (d, i) {
      var y = mt + i * linha, h = Math.min(24, linha * 0.58), yb = y + (linha - h) / 2;
      var w = Math.max(2, (d.count / topo) * area);
      texto(svg, ml - 8, yb + h / 2 + 4, d.name, { 'text-anchor': 'end' });
      var barra = el('rect', { x: ml, y: yb, width: w, height: h, rx: 2, fill: COR }, svg);
      texto(svg, ml + w + 6, yb + h / 2 + 4, d.count, { 'font-weight': 600, fill: COR });
      var zona = el('rect', { x: 0, y: y, width: largura, height: linha, fill: 'transparent' }, svg);
      zona.style.cursor = 'default';
      zona.addEventListener('mousemove', function (e) {
        var r = container.getBoundingClientRect();
        barra.setAttribute('opacity', '.8');
        tip.mostrar(e.clientX - r.left, e.clientY - r.top,
          '<b>' + d.name + '</b><br>' + plural(d.count) + ' (' + Math.round(d.count / total * 100) + '%)');
      });
      zona.addEventListener('mouseleave', function () { barra.removeAttribute('opacity'); tip.esconder(); });
    });
  }

  function linhaTempo(container, dados) {
    container.innerHTML = '';
    if (!dados.length) return vazio(container, 'Sem dados para exibir.');
    var largura = Math.max(container.clientWidth, 260), altura = Math.max(container.clientHeight, 200);
    var ml = 34, mr = 14, mt = 12, mb = 28;
    var max = Math.max.apply(null, dados.map(function (d) { return d.count; }).concat([1]));
    var ticks = marcas(max), topo = ticks[ticks.length - 1];
    var w = largura - ml - mr, h = altura - mt - mb, n = dados.length;
    function X(i) { return n === 1 ? ml + w / 2 : ml + (i / (n - 1)) * w; }
    function Y(v) { return mt + h - (v / topo) * h; }

    var svg = el('svg', { width: largura, height: altura, viewBox: '0 0 ' + largura + ' ' + altura, role: 'img',
      'aria-label': 'Alertas por dia: ' + dados.map(function (d) { return d.label + ' ' + d.count; }).join(', ') }, container);
    ticks.forEach(function (v) {
      el('line', { x1: ml, x2: largura - mr, y1: Y(v), y2: Y(v), stroke: GRADE, 'stroke-dasharray': '3 3' }, svg);
      texto(svg, ml - 6, Y(v) + 4, v, { 'text-anchor': 'end' });
    });
    var cada = Math.ceil(n / 8);
    dados.forEach(function (d, i) {
      // contando a partir do último dia, para o rótulo de hoje sempre aparecer sem encostar no anterior
      if ((n - 1 - i) % cada === 0) texto(svg, X(i), altura - 9, d.label, { 'text-anchor': 'middle' });
    });
    el('line', { x1: ml, x2: largura - mr, y1: Y(0), y2: Y(0), stroke: EIXO }, svg);

    var pts = dados.map(function (d, i) { return X(i) + ',' + Y(d.count); });
    if (n > 1) {
      el('polygon', { points: X(0) + ',' + Y(0) + ' ' + pts.join(' ') + ' ' + X(n - 1) + ',' + Y(0), fill: COR_CLARA }, svg);
      el('polyline', { points: pts.join(' '), fill: 'none', stroke: COR, 'stroke-width': 2, 'stroke-linejoin': 'round' }, svg);
    }
    dados.forEach(function (d, i) {
      if (n <= 14 || d.count > 0) el('circle', { cx: X(i), cy: Y(d.count), r: 3.2, fill: '#fff', stroke: COR, 'stroke-width': 2 }, svg);
    });

    var guia = el('line', { y1: mt, y2: Y(0), stroke: EIXO, 'stroke-dasharray': '3 3', visibility: 'hidden' }, svg);
    var foco = el('circle', { r: 5, fill: COR, visibility: 'hidden' }, svg);
    var tip = dica(container);
    var capa = el('rect', { x: ml, y: mt, width: w, height: h + 4, fill: 'transparent' }, svg);
    capa.addEventListener('mousemove', function (e) {
      var r = container.getBoundingClientRect(), px = e.clientX - r.left;
      var i = n === 1 ? 0 : Math.max(0, Math.min(n - 1, Math.round((px - ml) / w * (n - 1))));
      guia.setAttribute('x1', X(i)); guia.setAttribute('x2', X(i)); guia.setAttribute('visibility', 'visible');
      foco.setAttribute('cx', X(i)); foco.setAttribute('cy', Y(dados[i].count)); foco.setAttribute('visibility', 'visible');
      tip.mostrar(X(i), Y(dados[i].count), '<b>' + dados[i].label + '</b><br>' + (dados[i].count === 1 ? '1 alerta' : dados[i].count + ' alertas'));
    });
    capa.addEventListener('mouseleave', function () {
      guia.setAttribute('visibility', 'hidden'); foco.setAttribute('visibility', 'hidden'); tip.esconder();
    });
  }

  function ler(el_, atributo) {
    try { var v = JSON.parse(el_.getAttribute(atributo) || '[]'); return Array.isArray(v) ? v : []; }
    catch (e) { return []; }
  }

  function desenhar() {
    var epi = document.getElementById('epiChart'), tl = document.getElementById('timelineChart');
    if (epi) barras(epi, ler(epi, 'data-cats'));
    if (tl) linhaTempo(tl, ler(tl, 'data-days'));
  }

  window.safeGuardCharts = {
    render: function (cats, dias) {
      var epi = document.getElementById('epiChart'), tl = document.getElementById('timelineChart');
      if (epi) barras(epi, cats || []);
      if (tl) linhaTempo(tl, dias || []);
    }
  };

  var timer = null;
  function iniciar() { desenhar(); }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', iniciar); else iniciar();
  window.addEventListener('resize', function () { clearTimeout(timer); timer = setTimeout(desenhar, 150); });
})();

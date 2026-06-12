import React, { useState, useEffect, useRef } from 'react';
import * as signalR from '@microsoft/signalr';

function App() {
  // Config state
  const [serverUrl, setServerUrl] = useState('http://localhost:5212');
  const [eventId, setEventId] = useState('11111111-1111-1111-1111-111111111111');
  const [roundNumber, setRoundNumber] = useState(1);
  
  // Connection status
  const [connStatus, setConnStatus] = useState('disconnected'); // 'disconnected', 'connecting', 'connected'
  const hubConnectionRef = useRef(null);
  
  // Board state
  const [boardState, setBoardState] = useState({
    eventName: '-',
    roundStatus: '-',
    solveCount: 5,
    progress: {
      totalCompetitors: 0,
      completedCompetitors: 0,
      noShowCompetitors: 0,
      pendingCompetitors: 0,
      totalExpectedSolves: 0,
      submittedSolves: 0
    },
    groups: [],
    competitors: []
  });

  // Animation references
  const [flashedRow, setFlashedRow] = useState(null);
  const [flashedCell, setFlashedCell] = useState(null);

  // Terminal log state
  const [logs, setLogs] = useState([
    {
      time: new Date().toLocaleTimeString(),
      type: 'info',
      eventName: 'Initialized',
      payload: 'Vite Client ready. Enter configurations and click Connect.'
    }
  ]);
  const terminalBodyRef = useRef(null);

  // Auto-scroll terminal
  useEffect(() => {
    if (terminalBodyRef.current) {
      terminalBodyRef.current.scrollTop = terminalBodyRef.current.scrollHeight;
    }
  }, [logs]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (hubConnectionRef.current) {
        hubConnectionRef.current.stop();
      }
    };
  }, []);

  // Helper to add logs
  const addLog = (type, eventName, payload) => {
    setLogs(prev => [
      ...prev,
      {
        time: new Date().toLocaleTimeString(),
        type,
        eventName,
        payload
      }
    ]);
  };

  // Time formatter
  const formatTime = (ms) => {
    if (ms === null || ms === undefined) return '-';
    if (ms === 2147483647 || ms === 99999999) return 'DNF';
    return (ms / 1000).toFixed(2);
  };

  // REST API fetch
  const fetchState = async (url, evId, roundNo) => {
    try {
      addLog('info', '[REST API] Fetching Live Board state...', null);
      const res = await fetch(`${url}/api/live-board/events/${evId}/rounds/${roundNo}`);
      if (!res.ok) throw new Error(`HTTP Error ${res.status}`);
      const data = await res.json();
      
      addLog('success', '[REST API] Live Board state loaded', data);
      setBoardState(data);
    } catch (err) {
      addLog('error', `[REST API Error] ${err.message}`, null);
    }
  };

  // SignalR Handler
  const connectSignalR = (url, evId, roundNo) => {
    if (hubConnectionRef.current) {
      hubConnectionRef.current.stop();
    }

    addLog('info', '[SignalR] Initializing hub connection...', null);
    setConnStatus('connecting');

    const conn = new signalR.HubConnectionBuilder()
      .withUrl(`${url}/hubs/tournament`)
      .withAutomaticReconnect()
      .build();

    hubConnectionRef.current = conn;

    // Listeners
    conn.on('RoundStarted', (payload) => {
      addLog('success', 'Signal: RoundStarted', payload);
      
      setBoardState(prev => {
        // Mark no-show competitors
        const updatedCompetitors = prev.competitors.map(c => {
          if (payload.noShowCompetitorIds && payload.noShowCompetitorIds.includes(c.groupCompetitorId)) {
            return { ...c, competitorStatus: 'NO_SHOW' };
          }
          return c;
        });

        return {
          ...prev,
          roundStatus: payload.roundStatus,
          competitors: updatedCompetitors
        };
      });
    });

    conn.on('ResultSubmitted', (payload) => {
      addLog('success', 'Signal: ResultSubmitted', payload);
      
      const compId = payload.groupCompetitorId;
      const solveNum = payload.result.solveNumber;

      // Trigger animation highlights
      setFlashedRow(compId);
      setFlashedCell(`${compId}-${solveNum}`);

      setTimeout(() => {
        setFlashedRow(null);
        setFlashedCell(null);
      }, 1500);

      // Re-fetch rankings state from DB to ensure rank consistency
      fetchState(url, evId, roundNo);
    });

    conn.on('ResultCorrected', (payload) => {
      addLog('success', 'Signal: ResultCorrected', payload);
      
      const compId = payload.groupCompetitorId;
      const solveNum = payload.result.solveNumber;

      // Trigger animation highlights
      setFlashedRow(compId);
      setFlashedCell(`${compId}-${solveNum}`);

      setTimeout(() => {
        setFlashedRow(null);
        setFlashedCell(null);
      }, 1500);

      // Re-fetch rankings state from DB to ensure rank consistency
      fetchState(url, evId, roundNo);
    });

    conn.on('ResultsLocked', (payload) => {
      addLog('success', 'Signal: ResultsLocked', payload);
      setBoardState(prev => ({ ...prev, roundStatus: 'LOCKED' }));
    });

    conn.on('RoundCompleted', (payload) => {
      addLog('success', 'Signal: RoundCompleted', payload);
      setBoardState(prev => ({ ...prev, roundStatus: payload.roundStatus }));
    });

    // Connection lifecycle
    conn.onreconnected(() => {
      addLog('success', '[SignalR] Reconnected. Re-joining room & synchronizing...', null);
      setConnStatus('connected');
      
      conn.invoke('JoinEventRound', evId, parseInt(roundNo))
        .then(() => addLog('info', `[SignalR] Re-joined Room event:${evId}:round:${roundNo}`, null))
        .catch(err => addLog('error', `[SignalR Room Error] ${err.message}`, null));

      fetchState(url, evId, roundNo);
    });

    conn.onclose(() => {
      addLog('error', '[SignalR] Connection closed', null);
      setConnStatus('disconnected');
    });

    conn.start()
      .then(() => {
        addLog('success', '[SignalR] Connection established!', null);
        setConnStatus('connected');

        conn.invoke('JoinEventRound', evId, parseInt(roundNo))
          .then(() => addLog('info', `[SignalR] Joined Room event:${evId}:round:${roundNo}`, null))
          .catch(err => addLog('error', `[SignalR Room Error] ${err.message}`, null));
      })
      .catch(err => {
        addLog('error', `[SignalR Connection Error] ${err.message}`, null);
        setConnStatus('disconnected');
      });
  };

  const handleConnect = () => {
    if (!serverUrl || !eventId || !roundNumber) {
      alert('Please fill in Server URL, Event ID, and Round Number.');
      return;
    }
    fetchState(serverUrl, eventId, roundNumber);
    connectSignalR(serverUrl, eventId, roundNumber);
  };

  const handleLeave = async () => {
    if (hubConnectionRef.current && connStatus === 'connected') {
      try {
        await hubConnectionRef.current.invoke('LeaveEventRound', eventId, parseInt(roundNumber));
        addLog('info', `[SignalR] Left Room event:${eventId}:round:${roundNumber}`, null);
      } catch (err) {
        addLog('error', `[SignalR Leave Error] ${err.message}`, null);
      }
    }
  };

  // Get status badge properties
  const getBadgeClass = () => {
    if (connStatus === 'connected') return 'status-badge connected';
    if (connStatus === 'connecting') return 'status-badge connecting';
    return 'status-badge';
  };

  const getBadgeText = () => {
    if (connStatus === 'connected') return 'SignalR Connected';
    if (connStatus === 'connecting') return 'Connecting...';
    return 'SignalR Disconnected';
  };

  const solvesPercent = boardState.progress.totalExpectedSolves > 0
    ? Math.round((boardState.progress.submittedSolves / boardState.progress.totalExpectedSolves) * 100)
    : 0;

  return (
    <div className="container">
      {/* HEADER */}
      <header>
        <div className="logo-section">
          <h1>CubeNexus Live Board</h1>
          <p>Offline Tournament Realtime Monitoring Console (Vite React)</p>
        </div>
        <div className={getBadgeClass()}>
          <span className="dot"></span>
          <span>{getBadgeText()}</span>
        </div>
      </header>

      {/* SIDEBAR: CONTROLS */}
      <aside class="glass-card">
        <h2>🔌 Configuration</h2>
        
        <div className="form-group">
          <label>Backend Server API URL</label>
          <input 
            type="text" 
            value={serverUrl} 
            onChange={(e) => setServerUrl(e.target.value)} 
            placeholder="http://localhost:5212"
          />
        </div>

        <div className="form-group">
          <label>Event ID (GUID)</label>
          <input 
            type="text" 
            value={eventId} 
            onChange={(e) => setEventId(e.target.value)} 
            placeholder="Event GUID"
          />
        </div>

        <div className="form-group">
          <label>Round Number</label>
          <input 
            type="number" 
            value={roundNumber} 
            onChange={(e) => setRoundNumber(parseInt(e.target.value) || 1)} 
            min="1"
          />
        </div>

        <button onClick={handleConnect} className="btn">Connect & Load State</button>
        <button 
          onClick={handleLeave} 
          className="btn btn-secondary" 
          disabled={connStatus !== 'connected'}
        >
          Leave Room
        </button>

        <h2>📊 Round Status</h2>
        <div className="info-grid">
          <div className="info-tile">
            <span class="label">Round Name</span>
            <span className="value">{boardState.eventName}</span>
          </div>
          <div className="info-tile">
            <span class="label">Status</span>
            <span className="value">
              {boardState.roundStatus === 'ONGOING' ? (
                <span className="round-status-tag ongoing">ONGOING</span>
              ) : boardState.roundStatus === 'COMPLETED' ? (
                <span className="round-status-tag completed">COMPLETED</span>
              ) : boardState.roundStatus === 'LOCKED' ? (
                <span className="round-status-tag">LOCKED</span>
              ) : (
                <span className="round-status-tag">{boardState.roundStatus}</span>
              )}
            </span>
          </div>
        </div>

        <div className="progress-container">
          <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.8rem', fontWeight: '600' }}>
            <span style={{ color: 'var(--text-muted)' }}>SOLVES PROGRESS</span>
            <span>{boardState.progress.submittedSolves} / {boardState.progress.totalExpectedSolves} ({solvesPercent}%)</span>
          </div>
          <div className="progress-bar-bg">
            <div className="progress-bar-fill" style={{ width: `${solvesPercent}%` }}></div>
          </div>
        </div>

        <div className="info-grid">
          <div className="info-tile">
            <span class="label">Total Competitors</span>
            <span className="value">{boardState.progress.totalCompetitors}</span>
          </div>
          <div className="info-tile">
            <span class="label">Completed</span>
            <span className="value">{boardState.progress.completedCompetitors}</span>
          </div>
          <div className="info-tile">
            <span class="label">No Show</span>
            <span className="value">{boardState.progress.noShowCompetitors}</span>
          </div>
          <div className="info-tile">
            <span class="label">Pending</span>
            <span className="value">{boardState.progress.pendingCompetitors}</span>
          </div>
        </div>
      </aside>

      {/* MAIN PANEL: LIVE BOARD */}
      <main className="glass-card live-board-panel">
        <h2>
          📋 Live Rankings & Submissions
          <span style={{ fontSize: '0.8rem', fontWeight: 500, color: 'var(--text-muted)' }}>
            {' '}Format: {boardState.solveCount} Solves
          </span>
        </h2>

        <div className="table-wrapper">
          <table>
            <thead>
              <tr>
                <th style={{ width: '60px' }}>Rank</th>
                <th>Competitor</th>
                <th style={{ width: '100px' }}>Station</th>
                <th style={{ width: '120px' }}>Status</th>
                {Array.from({ length: boardState.solveCount }).map((_, i) => (
                  <th key={i} style={{ width: '70px', textAlign: 'center' }}>S{i + 1}</th>
                ))}
                <th>Best</th>
                <th>Average</th>
              </tr>
            </thead>
            <tbody>
              {boardState.competitors.length === 0 ? (
                <tr>
                  <td colSpan={boardState.solveCount + 6} style={{ textAlign: 'center', color: 'var(--text-muted)', padding: '2rem' }}>
                    Configure Backend Server and click Connect to load data.
                  </td>
                </tr>
              ) : (
                boardState.competitors.map(comp => {
                  const solveMap = {};
                  comp.results.forEach(res => {
                    solveMap[res.solveNumber] = res;
                  });

                  const isRowFlashed = flashedRow === comp.groupCompetitorId;

                  return (
                    <tr 
                      key={comp.groupCompetitorId}
                      className={isRowFlashed ? 'flash-updated-row' : ''}
                    >
                      <td>
                        {comp.rank ? <span className="rank-badge">{comp.rank}</span> : <span className="rank-badge">-</span>}
                      </td>
                      <td style={{ fontWeight: '600' }}>{comp.competitorName}</td>
                      <td>{comp.stationNumber ? `Station ${comp.stationNumber}` : '-'}</td>
                      <td>
                        <span className={`comp-status-tag ${comp.competitorStatus}`}>
                          {comp.competitorStatus}
                        </span>
                      </td>
                      {Array.from({ length: boardState.solveCount }).map((_, i) => {
                        const solveNum = i + 1;
                        const res = solveMap[solveNum];
                        const isCellFlashed = flashedCell === `${comp.groupCompetitorId}-${solveNum}`;
                        
                        let cellClass = 'solve-cell';
                        if (res) cellClass += ' has-val';
                        if (res?.isDnf) cellClass += ' dnf';
                        if (isCellFlashed) cellClass += ' flash-updated-cell';

                        return (
                          <td key={i} className={cellClass}>
                            {res ? (res.isDnf ? 'DNF' : formatTime(res.finalTimeMs)) : '-'}
                          </td>
                        );
                      })}
                      <td className="time-best">{comp.bestTimeMs ? formatTime(comp.bestTimeMs) : '-'}</td>
                      <td className="time-avg">{comp.averageTimeMs ? formatTime(comp.averageTimeMs) : '-'}</td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </main>

      {/* EVENT CONSOLE LOGGER */}
      <section className="terminal-panel">
        <div className="terminal-header">
          <div className="circles">
            <span className="c-1"></span>
            <span className="c-2"></span>
            <span className="c-3"></span>
          </div>
          <span>WebSocket Hub Monitor (Received Signals)</span>
          <span style={{ fontFamily: 'var(--font-mono)', fontSize: '0.75rem' }}>Terminal Active</span>
        </div>
        <div className="terminal-body" ref={terminalBodyRef}>
          {logs.map((log, index) => (
            <div key={index} className="log-entry">
              <div className={`log-meta ${log.type}`}>
                <span className="log-time">[{log.time}]</span>{' '}
                <span className="log-event">{log.eventName}</span>
              </div>
              {log.payload && (
                <pre className="log-json">
                  {typeof log.payload === 'string' 
                    ? log.payload 
                    : JSON.stringify(log.payload, null, 2)}
                </pre>
              )}
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}

export default App;

import { useEffect, useState } from 'react';
import { API_BASE_URL } from '../config';

export default function ConfirmEmailPage() {
  const [status, setStatus] = useState('loading');
  const [message, setMessage] = useState('Đang xác nhận email...');

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    const email = params.get('email');
    const token = params.get('token');

    if (!email || !token) {
      setStatus('error');
      setMessage('Liên kết không hợp lệ. Thiếu email hoặc token.');
      return;
    }

    fetch(`${API_BASE_URL}/api/auth/confirm-email`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, token }),
    })
      .then(async (res) => {
        const data = await res.json().catch(() => ({}));
        if (!res.ok) throw new Error(data.message || 'Xác nhận thất bại.');
        setStatus('success');
        setMessage(data.message || 'Xác nhận email thành công.');
      })
      .catch((err) => {
        setStatus('error');
        setMessage(err.message || 'Không thể xác nhận email.');
      });
  }, []);

  return (
    <div className="auth-page">
      <div className="auth-card">
        <h1>Xác nhận email</h1>
        <p className={`auth-status ${status}`}>{message}</p>
        {status === 'success' && (
          <p className="auth-hint">Bạn có thể đăng nhập vào ứng dụng CubeNexus.</p>
        )}
      </div>
    </div>
  );
}

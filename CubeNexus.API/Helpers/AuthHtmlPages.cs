using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace CubeNexus.API.Helpers;

public static class AuthHtmlPages
{
    public static ContentResult Page(string title, string bodyHtml, bool isError = false)
    {
        var accent = isError ? "#ef4444" : "#10b981";
        var html = new StringBuilder();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"vi\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\" />");
        html.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        html.AppendLine($"  <title>{WebUtility.HtmlEncode(title)} - CubeNexus</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    body { font-family: system-ui, sans-serif; background: #0b0f19; color: #f3f4f6; display: grid; place-items: center; min-height: 100vh; margin: 0; }");
        html.AppendLine("    .card { background: #111827; border: 1px solid #374151; border-radius: 12px; padding: 2rem; max-width: 480px; width: 90%; }");
        html.AppendLine($"    h1 {{ margin: 0 0 1rem; font-size: 1.5rem; color: {accent}; }}");
        html.AppendLine("    p { line-height: 1.6; color: #d1d5db; }");
        html.AppendLine("    label { display: block; margin: 1rem 0 0.25rem; font-size: 0.9rem; }");
        html.AppendLine("    input { width: 100%; padding: 0.6rem; border-radius: 8px; border: 1px solid #4b5563; background: #1f2937; color: #f3f4f6; box-sizing: border-box; }");
        html.AppendLine("    button { margin-top: 1rem; width: 100%; padding: 0.75rem; border: none; border-radius: 8px; background: #6366f1; color: white; font-weight: 600; cursor: pointer; }");
        html.AppendLine("    button:hover { background: #4f46e5; }");
        html.AppendLine("    .msg { margin-top: 1rem; display: none; }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine("  <div class=\"card\">");
        html.AppendLine($"    <h1>{WebUtility.HtmlEncode(title)}</h1>");
        html.AppendLine($"    {bodyHtml}");
        html.AppendLine("  </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");

        return new ContentResult
        {
            Content = html.ToString(),
            ContentType = "text/html; charset=utf-8"
        };
    }

    public static ContentResult ResetPasswordForm(string email, string token)
    {
        var safeEmail = WebUtility.HtmlEncode(email);
        var safeToken = WebUtility.HtmlEncode(token);
        var jsEmail = System.Text.Json.JsonSerializer.Serialize(email);
        var jsToken = System.Text.Json.JsonSerializer.Serialize(token);

        var body = new StringBuilder();
        body.AppendLine($"<p>Nhập mật khẩu mới cho tài khoản <strong>{safeEmail}</strong>.</p>");
        body.AppendLine("<form id=\"resetForm\">");
        body.AppendLine("  <label for=\"newPassword\">Mật khẩu mới</label>");
        body.AppendLine("  <input id=\"newPassword\" name=\"newPassword\" type=\"password\" required minlength=\"6\" />");
        body.AppendLine("  <label for=\"confirmPassword\">Xác nhận mật khẩu</label>");
        body.AppendLine("  <input id=\"confirmPassword\" name=\"confirmPassword\" type=\"password\" required minlength=\"6\" />");
        body.AppendLine("  <button type=\"submit\">Đặt lại mật khẩu</button>");
        body.AppendLine("</form>");
        body.AppendLine("<p id=\"result\" class=\"msg\"></p>");
        body.AppendLine("<script>");
        body.AppendLine("document.getElementById('resetForm').addEventListener('submit', async (e) => {");
        body.AppendLine("  e.preventDefault();");
        body.AppendLine("  const newPassword = document.getElementById('newPassword').value;");
        body.AppendLine("  const confirmPassword = document.getElementById('confirmPassword').value;");
        body.AppendLine("  const result = document.getElementById('result');");
        body.AppendLine("  if (newPassword !== confirmPassword) {");
        body.AppendLine("    result.style.display = 'block';");
        body.AppendLine("    result.style.color = '#ef4444';");
        body.AppendLine("    result.textContent = 'Mật khẩu xác nhận không khớp.';");
        body.AppendLine("    return;");
        body.AppendLine("  }");
        body.AppendLine("  try {");
        body.AppendLine("    const res = await fetch('/api/auth/reset-password', {");
        body.AppendLine("      method: 'POST',");
        body.AppendLine("      headers: { 'Content-Type': 'application/json' },");
        body.AppendLine($"      body: JSON.stringify({{ email: {jsEmail}, token: {jsToken}, newPassword, confirmNewPassword: confirmPassword }})");
        body.AppendLine("    });");
        body.AppendLine("    const data = await res.json();");
        body.AppendLine("    result.style.display = 'block';");
        body.AppendLine("    if (res.ok) {");
        body.AppendLine("      result.style.color = '#10b981';");
        body.AppendLine("      result.textContent = data.message || 'Đặt lại mật khẩu thành công.';");
        body.AppendLine("    } else {");
        body.AppendLine("      result.style.color = '#ef4444';");
        body.AppendLine("      result.textContent = data.message || 'Đặt lại mật khẩu thất bại.';");
        body.AppendLine("    }");
        body.AppendLine("  } catch {");
        body.AppendLine("    result.style.display = 'block';");
        body.AppendLine("    result.style.color = '#ef4444';");
        body.AppendLine("    result.textContent = 'Không thể kết nối tới server.';");
        body.AppendLine("  }");
        body.AppendLine("});");
        body.AppendLine("</script>");

        return Page("Đặt lại mật khẩu", body.ToString());
    }
}

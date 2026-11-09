// ========================================================
// HỆ THỐNG THƯ VIỆN & ĐẶT MƯỢN SÁCH PHÂN TÁN (ĐỀ TÀI 20)
// Frontend Controller: Authentication, RBAC, RabbitMQ Queue Monitor
// ========================================================

// 1. API Configuration
let API_BASE_URL = localStorage.getItem('LIBRARY_API_URL') || (
  window.location.port === '3000' || window.location.port === '80' || window.location.port === ''
    ? 'http://localhost:5000'
    : 'http://localhost:5000'
);

// Global State
let currentUser = null; // { id, username, fullName, studentCode, role, roleName, token }
let allBooks = [];
let allUsers = [];
let autoRefreshTimer = null;
let currentSelectedBook = null;

// ========================================================
// 2. Lifecycle & Authentication Check
// ========================================================
document.addEventListener('DOMContentLoaded', () => {
  const inputUrl = document.getElementById('inputApiUrl');
  if (inputUrl) inputUrl.value = API_BASE_URL;

  // Background health check
  checkHealth();
  setInterval(checkHealth, 10000);

  // Check saved authentication session
  const savedUserJson = localStorage.getItem('AUTH_USER');
  if (savedUserJson) {
    try {
      currentUser = JSON.parse(savedUserJson);
      initAuthenticatedState();
    } catch (e) {
      localStorage.removeItem('AUTH_USER');
      showLoginView();
    }
  } else {
    showLoginView();
  }
});

function showLoginView() {
  document.getElementById('loginView').style.display = 'flex';
  document.getElementById('appView').style.display = 'none';
  if (autoRefreshTimer) {
    clearInterval(autoRefreshTimer);
    autoRefreshTimer = null;
  }
}

function initAuthenticatedState() {
  document.getElementById('loginView').style.display = 'none';
  document.getElementById('appView').style.display = 'block';

  // Apply Role-Based Access Control
  applyRolePermissions(currentUser);

  // Load Initial Data
  loadUsers();
  loadBooks();
  loadAllReservations();
  startAutoRefresh();
}

// ========================================================
// 3. Login & Logout Handlers
// ========================================================
async function handleLogin(e) {
  if (e) e.preventDefault();
  const username = document.getElementById('loginUsername')?.value.trim();
  const password = document.getElementById('loginPassword')?.value.trim();

  if (!username || !password) {
    Swal.fire('Chú ý', 'Vui lòng nhập đầy đủ tên đăng nhập và mật khẩu.', 'warning');
    return;
  }

  await executeLogin(username, password);
}

function quickLogin(username, password) {
  const uInput = document.getElementById('loginUsername');
  const pInput = document.getElementById('loginPassword');
  if (uInput) uInput.value = username;
  if (pInput) pInput.value = password;
  executeLogin(username, password);
}

async function executeLogin(username, password) {
  const submitBtn = document.getElementById('btnLoginSubmit');
  if (submitBtn) {
    submitBtn.disabled = true;
    submitBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span> Đang xác thực...';
  }

  try {
    const res = await fetch(`${API_BASE_URL}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, password })
    });

    if (!res.ok) {
      const errData = await res.json().catch(() => ({}));
      throw new Error(errData.message || 'Tên đăng nhập hoặc mật khẩu không chính xác.');
    }

    const data = await res.json();
    currentUser = {
      ...data.user,
      token: data.token
    };

    localStorage.setItem('AUTH_USER', JSON.stringify(currentUser));

    Swal.fire({
      icon: 'success',
      title: 'Đăng nhập thành công!',
      html: `
        <div class="text-center">
          <p class="mb-1">Chào mừng <b>${currentUser.fullName}</b></p>
          <span class="badge ${currentUser.role === 1 ? 'bg-warning text-dark' : 'bg-success'} px-3 py-1">
            Vai trò: ${currentUser.role === 1 ? 'Thủ thư (Quản lý)' : 'Sinh viên (Độc giả)'}
          </span>
        </div>
      `,
      timer: 1800,
      showConfirmButton: false
    });

    initAuthenticatedState();
  } catch (err) {
    Swal.fire('Đăng nhập thất bại', err.message || 'Không thể kết nối đến máy chủ xác thực.', 'error');
  } finally {
    if (submitBtn) {
      submitBtn.disabled = false;
      submitBtn.innerHTML = '<i class="fa-solid fa-right-to-bracket me-2"></i> Đăng nhập vào hệ thống';
    }
  }
}

function handleLogout() {
  Swal.fire({
    title: 'Xác nhận đăng xuất?',
    text: 'Bạn sẽ quay trở lại màn hình đăng nhập vai trò.',
    icon: 'question',
    showCancelButton: true,
    confirmButtonText: 'Đăng xuất',
    cancelButtonText: 'Ở lại'
  }).then((result) => {
    if (result.isConfirmed) {
      localStorage.removeItem('AUTH_USER');
      currentUser = null;
      showLoginView();
      // Reset active tab to books
      const firstTab = document.getElementById('tab-books-link');
      if (firstTab && window.bootstrap) {
        new bootstrap.Tab(firstTab).show();
      }
    }
  });
}

// ========================================================
// 4. Role-Based Access Control (RBAC)
// ========================================================
function applyRolePermissions(user) {
  if (!user) return;

  const isLibrarian = (user.role === 1 || user.role === 'Librarian');

  // 1. Update Navigation Bar Info
  const navUserName = document.getElementById('navUserName');
  const navUserRoleBadge = document.getElementById('navUserRoleBadge');
  const navUserCode = document.getElementById('navUserCode');
  const userAvatarIcon = document.getElementById('userAvatarIcon');

  if (navUserName) navUserName.textContent = user.fullName;
  if (navUserCode) navUserCode.textContent = `[${user.studentCode || user.username}]`;

  if (navUserRoleBadge) {
    if (isLibrarian) {
      navUserRoleBadge.className = 'role-badge-pill role-badge-librarian';
      navUserRoleBadge.innerHTML = '<i class="fa-solid fa-user-shield me-1"></i>Thủ Thư';
      if (userAvatarIcon) userAvatarIcon.className = 'fa-solid fa-user-tie text-warning';
    } else {
      navUserRoleBadge.className = 'role-badge-pill role-badge-student';
      navUserRoleBadge.innerHTML = '<i class="fa-solid fa-graduation-cap me-1"></i>Sinh Viên';
      if (userAvatarIcon) userAvatarIcon.className = 'fa-solid fa-graduation-cap text-success';
    }
  }

  // 2. Update Role Notice Banner
  const roleNoticeBanner = document.getElementById('roleNoticeBanner');
  const roleNoticeIcon = document.getElementById('roleNoticeIcon');
  const roleNoticeTitle = document.getElementById('roleNoticeTitle');
  const roleNoticeDesc = document.getElementById('roleNoticeDesc');

  if (isLibrarian) {
    if (roleNoticeBanner) roleNoticeBanner.className = 'alert shadow-sm rounded-4 p-3 mb-4 d-flex flex-wrap align-items-center justify-content-between gap-3 border-warning bg-warning bg-opacity-10';
    if (roleNoticeIcon) {
      roleNoticeIcon.className = 'p-3 rounded-circle text-white bg-warning';
      roleNoticeIcon.innerHTML = '<i class="fa-solid fa-user-shield fa-lg"></i>';
    }
    if (roleNoticeTitle) roleNoticeTitle.innerHTML = `<span class="text-warning-emphasis">Tài khoản Quản lý: THỦ THƯ (${user.fullName})</span>`;
    if (roleNoticeDesc) roleNoticeDesc.innerHTML = 'Toàn quyền nghiệp vụ: Xác nhận giao sách (<b>Borrowed</b>), thu hồi trả hoàn kho (<b>Returned</b>), giám sát toàn bộ hàng đợi RabbitMQ và lịch sử độc giả.';
  } else {
    if (roleNoticeBanner) roleNoticeBanner.className = 'alert shadow-sm rounded-4 p-3 mb-4 d-flex flex-wrap align-items-center justify-content-between gap-3 border-success bg-success bg-opacity-10';
    if (roleNoticeIcon) {
      roleNoticeIcon.className = 'p-3 rounded-circle text-white bg-success';
      roleNoticeIcon.innerHTML = '<i class="fa-solid fa-graduation-cap fa-lg"></i>';
    }
    if (roleNoticeTitle) roleNoticeTitle.innerHTML = `<span class="text-success-emphasis">Tài khoản Độc giả: SINH VIÊN (${user.fullName} - MSSV: ${user.studentCode})</span>`;
    if (roleNoticeDesc) roleNoticeDesc.innerHTML = 'Quyền độc giả: Tra cứu danh mục sách, gửi yêu cầu đặt mượn sách qua hàng đợi RabbitMQ, theo dõi trạng thái xử lý và xem lịch sử cá nhân.';
  }

  // 3. Tab Visibility (Nghiệp vụ thủ thư)
  const tabLibrarianItem = document.getElementById('tab-librarian-item');
  if (tabLibrarianItem) {
    tabLibrarianItem.style.display = isLibrarian ? 'block' : 'none';
  }

  // If student was on librarian tab, switch back
  if (!isLibrarian) {
    const activeTab = document.querySelector('.nav-link.active');
    if (activeTab && activeTab.id === 'tab-librarian-link') {
      const booksTab = document.getElementById('tab-books-link');
      if (booksTab && window.bootstrap) {
        new bootstrap.Tab(booksTab).show();
      }
    }
  }

  // 4. User History Controls
  const librarianUserSelectContainer = document.getElementById('librarianUserSelectContainer');
  const userHistorySubtitle = document.getElementById('userHistorySubtitle');
  if (librarianUserSelectContainer) {
    librarianUserSelectContainer.style.display = isLibrarian ? 'block' : 'none';
  }
  if (userHistorySubtitle) {
    userHistorySubtitle.innerHTML = isLibrarian
      ? 'Tra cứu và kiểm soát lịch sử mượn sách của bất kỳ sinh viên nào trong thư viện.'
      : `Lịch sử các đơn đặt mượn và tài liệu mượn của bạn (<b>${user.fullName}</b>).`;
  }
}

// Save API URL configuration
function saveApiUrl() {
  const url = document.getElementById('inputApiUrl').value.trim();
  if (url) {
    API_BASE_URL = url.replace(/\/+$/, '');
    localStorage.setItem('LIBRARY_API_URL', API_BASE_URL);
    bootstrap.Modal.getInstance(document.getElementById('configModal'))?.hide();
    Swal.fire('Thành công', `Đã lưu API Base URL: ${API_BASE_URL}`, 'success');
    checkHealth();
    loadBooks();
    loadUsers();
    loadAllReservations();
  }
}

// ========================================================
// 5. System Health Check
// ========================================================
async function checkHealth() {
  try {
    const res = await fetch(`${API_BASE_URL}/health`);
    if (res.ok) {
      const data = await res.json();
      updateStatusBadge('api', true, 'Online');
      updateStatusBadge('db', data.components?.database === 'Connected', data.components?.database);
      updateStatusBadge('rabbit', data.components?.rabbitMQ === 'Connected', data.components?.rabbitMQ);
    } else {
      updateStatusBadge('api', false, 'Degraded');
      updateStatusBadge('db', false, 'Unknown');
      updateStatusBadge('rabbit', false, 'Unknown');
    }
  } catch (err) {
    updateStatusBadge('api', false, 'Offline');
    updateStatusBadge('db', false, 'Offline');
    updateStatusBadge('rabbit', false, 'Offline');
  }
}

function updateStatusBadge(type, isOk, text) {
  const dot = document.getElementById(`dot-${type}`);
  const textEl = document.getElementById(`text-${type}`);
  if (dot && textEl) {
    dot.className = `status-dot ${isOk ? 'online' : 'offline'}`;
    textEl.textContent = text || (isOk ? 'Online' : 'Offline');
  }
}

// ========================================================
// 6. Users Catalog
// ========================================================
async function loadUsers() {
  try {
    const res = await fetch(`${API_BASE_URL}/users`);
    if (res.ok) {
      allUsers = await res.json();

      // Populate filter in Tab 4 for Librarian
      const filterSelect = document.getElementById('userHistoryFilterSelect');
      if (filterSelect) {
        filterSelect.innerHTML = '<option value="">-- Chọn sinh viên để xem lịch sử --</option>' +
          allUsers.filter(u => u.role === 0 || u.role === 'Student').map(u => `
            <option value="${u.id}">${u.fullName} (${u.studentCode})</option>
          `).join('');

        if (currentUser && currentUser.role === 1 && filterSelect.options.length > 1) {
          filterSelect.selectedIndex = 1;
        }
      }
    }
  } catch (err) {
    console.error('Failed to load users:', err);
  }
}

function getEffectiveReservationUserId() {
  if (!currentUser) return 1;
  // If Librarian, can borrow as test reader or default user
  return currentUser.id;
}

// ========================================================
// 7. Books Catalog & Details
// ========================================================
async function loadBooks() {
  const grid = document.getElementById('booksGrid');
  if (!grid) return;
  grid.innerHTML = `
    <div class="col-12 text-center py-5">
      <div class="spinner-border text-primary" role="status"></div>
      <p class="mt-2 text-muted">Đang tải danh sách tài liệu thư viện...</p>
    </div>
  `;

  try {
    const res = await fetch(`${API_BASE_URL}/books`);
    if (!res.ok) throw new Error('API returned ' + res.status);
    allBooks = await res.json();

    // Populate Category filter
    populateCategories();

    // Populate stress test selector
    populateStressBookSelect();

    // Render cards
    renderBooks(allBooks);
  } catch (err) {
    grid.innerHTML = `
      <div class="col-12 text-center py-5">
        <i class="fa-solid fa-triangle-exclamation text-danger fa-3x mb-3"></i>
        <h5 class="text-danger">Không thể kết nối đến Library API</h5>
        <p class="text-muted small">Hãy chắc chắn rằng backend API đang chạy tại <code>${API_BASE_URL}</code></p>
        <button class="btn btn-primary btn-sm" onclick="loadBooks()">Thử lại</button>
      </div>
    `;
  }
}

function populateCategories() {
  const select = document.getElementById('bookCategorySelect');
  if (!select) return;
  const categories = [...new Set(allBooks.map(b => b.category).filter(Boolean))];
  select.innerHTML = '<option value="">Tất cả thể loại</option>' +
    categories.map(c => `<option value="${c}">${c}</option>`).join('');
}

function populateStressBookSelect() {
  const select = document.getElementById('stressBookSelect');
  if (!select) return;
  const sorted = [...allBooks].sort((a, b) => a.availableCopies - b.availableCopies);
  select.innerHTML = sorted.map(b => `
    <option value="${b.id}" ${b.availableCopies === 1 ? 'selected' : ''}>
      ${b.title} (Còn: ${b.availableCopies}/${b.totalCopies} bản)
    </option>
  `).join('');
}

function renderBooks(books) {
  const grid = document.getElementById('booksGrid');
  if (!grid) return;
  if (!books || books.length === 0) {
    grid.innerHTML = '<div class="col-12 text-center py-5 text-muted">Không tìm thấy cuốn sách nào phù hợp.</div>';
    return;
  }

  const themes = ['theme-1', 'theme-2', 'theme-3', 'theme-4'];

  grid.innerHTML = books.map((book, idx) => {
    const isAvailable = book.availableCopies > 0;
    const theme = themes[idx % themes.length];

    return `
      <div class="col-md-6 col-lg-4">
        <div class="card-custom h-100 d-flex flex-column">
          <div class="book-cover ${theme} p-3">
            <span class="badge bg-dark bg-opacity-50 text-white rounded-pill px-2 py-1 small">
              <i class="fa-solid fa-tag me-1"></i>${book.category || 'Tài liệu'}
            </span>
            <i class="fa-solid fa-book-open book-cover-icon"></i>
          </div>

          <div class="p-3 d-flex flex-column flex-grow-1">
            <h6 class="fw-bold text-dark text-truncate mb-1" title="${book.title}">${book.title}</h6>
            <p class="text-muted small mb-2"><i class="fa-solid fa-pen-nib me-1"></i>${book.author}</p>
            <p class="text-secondary small text-truncate-2 mb-3 flex-grow-1" style="display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden;">
              ${book.description}
            </p>

            <div class="d-flex align-items-center justify-content-between pt-2 border-top mb-3">
              <span class="small text-muted">Mã ISBN: <code>${book.isbn}</code></span>
              <span class="badge ${isAvailable ? 'bg-success' : 'bg-danger'} rounded-pill px-2 py-1">
                ${isAvailable ? `Còn ${book.availableCopies}/${book.totalCopies} bản` : 'Hết bản khả dụng'}
              </span>
            </div>

            <div class="d-flex gap-2">
              <button class="btn btn-outline-secondary btn-sm flex-fill" onclick="viewBookDetail(${book.id})">
                <i class="fa-solid fa-circle-info me-1"></i> Chi tiết
              </button>
              <button class="btn ${isAvailable ? 'btn-primary' : 'btn-danger'} btn-sm flex-fill fw-medium" onclick="requestReservation(${book.id})">
                <i class="fa-solid fa-bookmark me-1"></i> ${isAvailable ? 'Đặt mượn' : 'Đặt (Thử nghiệm)'}
              </button>
            </div>
          </div>
        </div>
      </div>
    `;
  }).join('');
}

function filterBooks() {
  const searchTerm = (document.getElementById('bookSearchInput')?.value || '').toLowerCase();
  const category = (document.getElementById('bookCategorySelect')?.value || '').toLowerCase();

  const filtered = allBooks.filter(b => {
    const matchSearch = !searchTerm ||
      b.title.toLowerCase().includes(searchTerm) ||
      b.author.toLowerCase().includes(searchTerm) ||
      b.isbn.toLowerCase().includes(searchTerm);
    const matchCategory = !category || b.category.toLowerCase() === category;
    return matchSearch && matchCategory;
  });

  renderBooks(filtered);
}

function viewBookDetail(bookId) {
  const book = allBooks.find(b => b.id === bookId);
  if (!book) return;

  currentSelectedBook = book;
  document.getElementById('detailBookTitle').textContent = book.title;
  document.getElementById('detailBookAuthor').textContent = book.author;
  document.getElementById('detailBookCategory').textContent = book.category;
  document.getElementById('detailBookISBN').textContent = book.isbn;
  document.getElementById('detailBookDescription').textContent = book.description;
  document.getElementById('detailTotalCopies').textContent = `${book.totalCopies} bản`;
  document.getElementById('detailAvailableCopies').textContent = `${book.availableCopies} bản`;

  const btnReserve = document.getElementById('detailBtnReserve');
  if (book.availableCopies > 0) {
    btnReserve.className = 'btn btn-primary w-100 fw-bold';
    btnReserve.innerHTML = '<i class="fa-solid fa-bookmark me-1"></i> Đặt mượn cuốn sách này';
  } else {
    btnReserve.className = 'btn btn-danger w-100 fw-bold';
    btnReserve.innerHTML = '<i class="fa-solid fa-triangle-exclamation me-1"></i> Đặt thử (Sẽ bị từ chối do hết sách)';
  }

  const tbody = document.getElementById('detailCopiesTableBody');
  if (book.copies && book.copies.length > 0) {
    tbody.innerHTML = book.copies.map(c => {
      let badgeClass = 'bg-success';
      if (c.status === 1) badgeClass = 'bg-warning text-dark';
      else if (c.status === 2) badgeClass = 'bg-primary';
      else if (c.status === 3) badgeClass = 'bg-secondary';

      return `
        <tr>
          <td><code>${c.barcode}</code></td>
          <td><span class="badge ${badgeClass}">${c.statusText}</span></td>
          <td>${c.currentReservationId ? `#${c.currentReservationId}` : '<span class="text-muted">None</span>'}</td>
        </tr>
      `;
    }).join('');
  } else {
    tbody.innerHTML = '<tr><td colspan="3" class="text-center text-muted">Không có bản sao cụ thể nào.</td></tr>';
  }

  const modal = new bootstrap.Modal(document.getElementById('bookDetailModal'));
  modal.show();
}

function reserveFromDetail() {
  if (currentSelectedBook) {
    bootstrap.Modal.getInstance(document.getElementById('bookDetailModal'))?.hide();
    requestReservation(currentSelectedBook.id);
  }
}

// ========================================================
// 8. Place Reservation (POST /reservations -> RabbitMQ)
// ========================================================
async function requestReservation(bookId) {
  if (!currentUser) {
    Swal.fire('Chưa đăng nhập', 'Vui lòng đăng nhập để đặt mượn sách.', 'warning');
    return;
  }

  const userId = currentUser.id;
  const book = allBooks.find(b => b.id === bookId);

  const confirmResult = await Swal.fire({
    title: 'Xác nhận đặt mượn sách',
    html: `
      <div class="text-start small">
        <p class="mb-1"><b>Người yêu cầu:</b> ${currentUser.fullName} (${currentUser.studentCode || currentUser.username})</p>
        <p class="mb-1"><b>Vai trò:</b> <span class="badge ${currentUser.role === 1 ? 'bg-warning text-dark' : 'bg-success'}">${currentUser.role === 1 ? 'Thủ thư' : 'Sinh viên'}</span></p>
        <p class="mb-1"><b>Tài liệu:</b> ${book ? book.title : `Book #${bookId}`}</p>
        <p class="mb-0 text-muted">Yêu cầu sẽ được đưa vào <b>RabbitMQ Broker</b> để Worker kiểm tra số bản khả dụng và xử lý bất đồng bộ.</p>
      </div>
    `,
    icon: 'question',
    showCancelButton: true,
    confirmButtonText: 'Gửi yêu cầu ngay',
    cancelButtonText: 'Hủy'
  });

  if (!confirmResult.isConfirmed) return;

  try {
    Swal.fire({
      title: 'Đang gửi vào RabbitMQ...',
      text: 'Vui lòng chờ giây lát',
      allowOutsideClick: false,
      didOpen: () => Swal.showLoading()
    });

    const res = await fetch(`${API_BASE_URL}/reservations`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ userId, bookId })
    });

    if (!res.ok) {
      const errData = await res.json().catch(() => ({}));
      throw new Error(errData.message || 'Gửi yêu cầu thất bại.');
    }

    const createdReservation = await res.json();

    Swal.fire({
      title: 'Đã đưa vào RabbitMQ!',
      html: `
        <div class="text-start small">
          <p class="mb-1"><b>Mã đơn:</b> #${createdReservation.id}</p>
          <p class="mb-1"><b>Trạng thái:</b> <span class="badge badge-Pending"><span class="pulse-indicator me-1"></span> Pending (Đang xử lý)</span></p>
          <p class="mb-0 text-muted">Worker đang kiểm tra số bản khả dụng trong Database. Trạng thái sẽ được cập nhật tự động trong vài giây!</p>
        </div>
      `,
      icon: 'success',
      timer: 2500,
      showConfirmButton: false
    });

    loadAllReservations();
    loadBooks();
  } catch (err) {
    Swal.fire('Lỗi', err.message || 'Không thể tạo yêu cầu đặt mượn.', 'error');
  }
}

// ========================================================
// 9. Reservations Queue Monitoring & Action Buttons
// ========================================================
async function loadAllReservations() {
  const tbody = document.getElementById('reservationsTableBody');
  if (!tbody) return;

  const isLibrarian = currentUser && (currentUser.role === 1 || currentUser.role === 'Librarian');

  try {
    const res = await fetch(`${API_BASE_URL}/reservations`);
    if (!res.ok) return;

    const list = await res.json();
    const pendingCount = list.filter(r => r.status === 0).length;
    const badgePending = document.getElementById('badgePendingCount');
    if (badgePending) {
      if (pendingCount > 0) {
        badgePending.textContent = pendingCount;
        badgePending.style.display = 'inline-block';
      } else {
        badgePending.style.display = 'none';
      }
    }

    if (!list || list.length === 0) {
      tbody.innerHTML = '<tr><td colspan="8" class="text-center py-4 text-muted">Chưa có đơn đặt mượn nào trong hệ thống.</td></tr>';
      return;
    }

    tbody.innerHTML = list.map(item => {
      const badgeHtml = renderStatusBadge(item.status, item.statusText);
      const formattedDate = new Date(item.createdAt).toLocaleString('vi-VN');

      let actionHtml = '-';
      if (isLibrarian) {
        if (item.status === 1) { // Reserved
          actionHtml = `
            <button class="btn btn-sm btn-outline-success py-0 px-2 fw-medium" onclick="confirmBorrow(${item.id})">
              <i class="fa-solid fa-hand-holding me-1"></i> Giao sách
            </button>
          `;
        } else if (item.status === 3) { // Borrowed
          actionHtml = `
            <button class="btn btn-sm btn-outline-primary py-0 px-2 fw-medium" onclick="confirmReturn(${item.id})">
              <i class="fa-solid fa-rotate-left me-1"></i> Trả sách
            </button>
          `;
        }
      } else {
        // For Students, actions are disabled with badge
        if (item.status === 1) {
          actionHtml = `<span class="badge bg-light text-secondary border">Chờ nhận tại quầy</span>`;
        } else if (item.status === 3) {
          actionHtml = `<span class="badge bg-light text-primary border">Đang giữ sách</span>`;
        }
      }

      return `
        <tr>
          <td><b class="text-primary">#${item.id}</b></td>
          <td>
            <div class="fw-semibold">${item.userName}</div>
            <div class="text-muted small">${item.studentCode}</div>
          </td>
          <td>
            <div class="fw-medium text-truncate" style="max-width: 250px;" title="${item.bookTitle}">${item.bookTitle}</div>
            <div class="text-muted small">${item.bookAuthor}</div>
          </td>
          <td><code>${item.copyBarcode || '<span class="text-muted">Chưa gán</span>'}</code></td>
          <td>${badgeHtml}</td>
          <td class="small text-secondary" style="max-width: 280px;">${item.notes || '-'}</td>
          <td class="small text-muted">${formattedDate}</td>
          <td class="text-center">${actionHtml}</td>
        </tr>
      `;
    }).join('');
  } catch (err) {
    console.error('Failed to load reservations:', err);
  }
}

function renderStatusBadge(status, text) {
  let icon = 'fa-clock';
  if (status === 1) icon = 'fa-circle-check';
  else if (status === 2) icon = 'fa-circle-xmark';
  else if (status === 3) icon = 'fa-book-open-reader';
  else if (status === 4) icon = 'fa-box-archive';

  return `
    <span class="badge-status badge-${text}">
      <i class="fa-solid ${icon}"></i> ${text}
    </span>
  `;
}

function startAutoRefresh() {
  if (autoRefreshTimer) clearInterval(autoRefreshTimer);
  autoRefreshTimer = setInterval(() => {
    const sw = document.getElementById('autoRefreshSwitch');
    if (sw && sw.checked) {
      loadAllReservations();
    }
  }, 3000);
}

function toggleAutoRefresh() {
  const sw = document.getElementById('autoRefreshSwitch');
  if (sw && !sw.checked) {
    if (autoRefreshTimer) clearInterval(autoRefreshTimer);
  } else {
    startAutoRefresh();
  }
}

// ========================================================
// 10. Librarian Operations (Fulfillment)
// ========================================================
async function loadLibrarianTasks() {
  if (!currentUser || (currentUser.role !== 1 && currentUser.role !== 'Librarian')) {
    return;
  }

  try {
    const res = await fetch(`${API_BASE_URL}/reservations`);
    if (!res.ok) return;
    const all = await res.json();

    const reserved = all.filter(r => r.status === 1);
    const borrowed = all.filter(r => r.status === 3);

    document.getElementById('countReserved').textContent = reserved.length;
    document.getElementById('countBorrowed').textContent = borrowed.length;

    // Render Reserved (Awaiting pickup)
    const reservedList = document.getElementById('reservedList');
    if (reserved.length === 0) {
      reservedList.innerHTML = '<div class="text-center py-4 text-muted">Không có đơn nào đang chờ nhận sách.</div>';
    } else {
      reservedList.innerHTML = reserved.map(r => `
        <div class="list-group-item p-3 border rounded-3 mb-2 d-flex justify-content-between align-items-center">
          <div>
            <div class="fw-bold text-dark">Đơn #${r.id} - ${r.userName} (${r.studentCode})</div>
            <div class="small text-muted">Sách: <b>${r.bookTitle}</b> | Mã bản: <code>${r.copyBarcode}</code></div>
            <div class="small text-secondary">${r.notes}</div>
          </div>
          <button class="btn btn-sm btn-success fw-medium text-nowrap ms-2" onclick="confirmBorrow(${r.id})">
            <i class="fa-solid fa-check me-1"></i> Xác nhận giao sách
          </button>
        </div>
      `).join('');
    }

    // Render Borrowed (Awaiting return)
    const borrowedList = document.getElementById('borrowedList');
    if (borrowed.length === 0) {
      borrowedList.innerHTML = '<div class="text-center py-4 text-muted">Không có cuốn sách nào đang được mượn.</div>';
    } else {
      borrowedList.innerHTML = borrowed.map(r => `
        <div class="list-group-item p-3 border rounded-3 mb-2 d-flex justify-content-between align-items-center">
          <div>
            <div class="fw-bold text-dark">Đơn #${r.id} - ${r.userName} (${r.studentCode})</div>
            <div class="small text-muted">Sách: <b>${r.bookTitle}</b> | Mã bản: <code>${r.copyBarcode}</code></div>
            <div class="small text-secondary">${r.notes}</div>
          </div>
          <button class="btn btn-sm btn-primary fw-medium text-nowrap ms-2" onclick="confirmReturn(${r.id})">
            <i class="fa-solid fa-rotate-left me-1"></i> Xác nhận trả sách
          </button>
        </div>
      `).join('');
    }
  } catch (err) {
    console.error('Failed to load librarian tasks:', err);
  }
}

async function confirmBorrow(reservationId) {
  try {
    const res = await fetch(`${API_BASE_URL}/reservations/${reservationId}/borrow`, {
      method: 'POST'
    });
    if (!res.ok) throw new Error('Không thể nhận sách');
    Swal.fire('Thành công', `Đơn #${reservationId} đã chuyển sang trạng thái: Đã nhận sách (Borrowed)!`, 'success');
    loadLibrarianTasks();
    loadAllReservations();
    loadBooks();
  } catch (err) {
    Swal.fire('Lỗi', err.message, 'error');
  }
}

async function confirmReturn(reservationId) {
  try {
    const res = await fetch(`${API_BASE_URL}/reservations/${reservationId}/return`, {
      method: 'POST'
    });
    if (!res.ok) throw new Error('Không thể hoàn trả sách');
    Swal.fire('Thành công', `Đơn #${reservationId} đã trả sách thành công! 1 bản sao đã được hoàn lại kho.`, 'success');
    loadLibrarianTasks();
    loadAllReservations();
    loadBooks();
  } catch (err) {
    Swal.fire('Lỗi', err.message, 'error');
  }
}

// ========================================================
// 11. User Borrowing History (RBAC filtered)
// ========================================================
async function loadUserReservations() {
  if (!currentUser) return;

  const isLibrarian = (currentUser.role === 1 || currentUser.role === 'Librarian');
  let targetUserId = currentUser.id;

  if (isLibrarian) {
    const select = document.getElementById('userHistoryFilterSelect');
    if (select && select.value) {
      targetUserId = parseInt(select.value);
    }
  }

  const tbody = document.getElementById('userReservationsTableBody');
  tbody.innerHTML = '<tr><td colspan="6" class="text-center py-4 text-muted">Đang tải lịch sử của độc giả...</td></tr>';

  try {
    const res = await fetch(`${API_BASE_URL}/users/${targetUserId}/reservations`);
    if (!res.ok) throw new Error('Không thể tải lịch sử người dùng');
    const list = await res.json();

    if (!list || list.length === 0) {
      tbody.innerHTML = '<tr><td colspan="6" class="text-center py-4 text-muted">Độc giả này chưa có đơn đặt mượn nào.</td></tr>';
      return;
    }

    tbody.innerHTML = list.map(item => {
      const badgeHtml = renderStatusBadge(item.status, item.statusText);
      const formattedDate = new Date(item.createdAt).toLocaleString('vi-VN');
      return `
        <tr>
          <td><b class="text-primary">#${item.id}</b></td>
          <td><b>${item.bookTitle}</b></td>
          <td><code>${item.copyBarcode || '-'}</code></td>
          <td>${badgeHtml}</td>
          <td class="small text-secondary">${item.notes || '-'}</td>
          <td class="small text-muted">${formattedDate}</td>
        </tr>
      `;
    }).join('');
  } catch (err) {
    tbody.innerHTML = `<tr><td colspan="6" class="text-center text-danger py-4">${err.message}</td></tr>`;
  }
}

// ========================================================
// 12. Concurrency Race Condition Demo
// ========================================================
async function triggerStressTest() {
  const bookSelect = document.getElementById('stressBookSelect');
  const bookId = parseInt(bookSelect?.value);
  if (!bookId) {
    Swal.fire('Lỗi', 'Vui lòng chọn một cuốn sách để thử nghiệm tranh chấp.', 'warning');
    return;
  }

  const container = document.getElementById('stressResultsContainer');
  const btnRun = document.getElementById('btnStressRun');

  if (allUsers.length < 2) {
    Swal.fire('Lỗi', 'Cần ít nhất 2 người dùng trong hệ thống để mô phỏng tranh chấp.', 'warning');
    return;
  }

  // Pick 2 student users
  const students = allUsers.filter(u => u.role === 0 || u.role === 'Student');
  const userA = students[0] || allUsers[0];
  const userB = students[1] || allUsers[1];

  btnRun.disabled = true;
  btnRun.innerHTML = '<span class="spinner-border spinner-border-sm me-2"></span> Đang gửi 2 Request Song Song...';

  container.innerHTML = `
    <div class="col-12 text-center py-3">
      <div class="spinner-grow text-warning" role="status"></div>
      <p class="mt-2 text-muted">Đang bắn 2 request đồng thời qua HTTP tới API & RabbitMQ...</p>
    </div>
  `;

  try {
    const [resA, resB] = await Promise.all([
      fetch(`${API_BASE_URL}/reservations`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId: userA.id, bookId })
      }),
      fetch(`${API_BASE_URL}/reservations`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ userId: userB.id, bookId })
      })
    ]);

    const dataA = await resA.json();
    const dataB = await resB.json();

    container.innerHTML = `
      <div class="col-12 mb-2">
        <div class="alert alert-info py-2 mb-0 small">
          <i class="fa-solid fa-paper-plane me-1"></i> Cả 2 request đã được API tiếp nhận ngay lập tức và đẩy vào RabbitMQ queue <code>reservation.requests</code>.
          Đang chờ Worker xử lý tuần tự và cập nhật kết quả...
        </div>
      </div>
      <div class="col-md-6">
        <div class="card p-3 border shadow-sm" id="stressCardA">
          <h6 class="fw-bold mb-1">Request 1: ${userA.fullName}</h6>
          <div class="small text-muted mb-2">Mã đơn: #${dataA.id}</div>
          <div id="stressStatusA"><span class="badge badge-Pending"><span class="pulse-indicator me-1"></span> Đang chờ Worker...</span></div>
        </div>
      </div>
      <div class="col-md-6">
        <div class="card p-3 border shadow-sm" id="stressCardB">
          <h6 class="fw-bold mb-1">Request 2: ${userB.fullName}</h6>
          <div class="small text-muted mb-2">Mã đơn: #${dataB.id}</div>
          <div id="stressStatusB"><span class="badge badge-Pending"><span class="pulse-indicator me-1"></span> Đang chờ Worker...</span></div>
        </div>
      </div>
    `;

    let attempts = 0;
    const pollInterval = setInterval(async () => {
      attempts++;
      try {
        const [pollA, pollB] = await Promise.all([
          fetch(`${API_BASE_URL}/reservations/${dataA.id}`).then(r => r.json()),
          fetch(`${API_BASE_URL}/reservations/${dataB.id}`).then(r => r.json())
        ]);

        if (pollA.status !== 0) {
          document.getElementById('stressStatusA').innerHTML = `
            ${renderStatusBadge(pollA.status, pollA.statusText)}
            <div class="small text-secondary mt-2">${pollA.notes}</div>
          `;
        }

        if (pollB.status !== 0) {
          document.getElementById('stressStatusB').innerHTML = `
            ${renderStatusBadge(pollB.status, pollB.statusText)}
            <div class="small text-secondary mt-2">${pollB.notes}</div>
          `;
        }

        if ((pollA.status !== 0 && pollB.status !== 0) || attempts > 10) {
          clearInterval(pollInterval);
          btnRun.disabled = false;
          btnRun.innerHTML = '<i class="fa-solid fa-bolt-lightning me-2"></i> Bấm để gửi 2 Request Song Song Cùng Lúc!';
          loadBooks();
          loadAllReservations();
        }
      } catch (e) {
        console.error(e);
      }
    }, 1000);

  } catch (err) {
    btnRun.disabled = false;
    btnRun.innerHTML = '<i class="fa-solid fa-bolt-lightning me-2"></i> Bấm để gửi 2 Request Song Song Cùng Lúc!';
    container.innerHTML = `<div class="col-12 text-danger text-center py-3">Lỗi khi thực hiện stress test: ${err.message}</div>`;
  }
}

# Tài liệu Test Cases cho Page API

Tài liệu này tổng hợp các test case (đúng/sai) để bạn sử dụng kiểm tra trên giao diện Swagger.

## Thông tin giả định
- **Valid Page ID**: `123456789` (Thay bằng Page ID thật của bạn)
- **Invalid Page ID**: `invalid_page_id_000`
- **Valid Post ID**: `987654321` (Thay bằng ID bài viết thật)
- **Invalid Post ID**: `invalid_post_id_999`
- **Page Access Token**: Đã cấu hình trong `appsettings.json`

---

## 1. GET /api/page/{pageId} (Lấy thông tin Page)
| Case | Input `pageId` | Mong đợi (Expected Result) |
| :--- | :--- | :--- |
| **Đúng** | `{PageId thật}` | Trả về JSON thông tin Page (Name, ID, ...), Status 200 OK. |
| **Sai (ID không tồn tại)** | `99999999999999` | Trả về thông báo lỗi từ Facebook (Object not found), Status 500 hoặc 400. |
| **Sai (Sai định dạng)** | `abc-xyz` | Trả về lỗi định dạng ID, Status 500. |

---

## 2. GET /api/page/{pageId}/posts (Lấy danh sách bài viết)
| Case | Input `pageId` | Mong đợi (Expected Result) |
| :--- | :--- | :--- |
| **Đúng** | `{PageId thật}` | Trả về mảng `data` chứa các bài post, Status 200 OK. |
| **Sai (ID không tồn tại)** | `invalid_id` | Lỗi Facebook API, Status 500. |

---

## 3. POST /api/page/{pageId}/posts (Tạo bài viết mới)
- **Body**: `{ "message": "Nội dung bài viết" }`

| Case | Input | Mong đợi (Expected Result) |
| :--- | :--- | :--- |
| **Đúng** | Body hợp lệ + ID thật | Trả về ID của bài viết vừa tạo, Status 200 OK. |
| **Sai (Nội dung trống)** | `{ "message": "" }` | Facebook có thể báo lỗi thiếu content, Status 400/500. |
| **Sai (Page ID sai)** | ID không tồn tại | Trả về lỗi quyền hạn hoặc ID không tồn tại, Status 400/500. |

---

## 4. DELETE /api/page/post/{postId} (Xóa bài viết)
| Case | Input `postId` | Mong đợi (Expected Result) |
| :--- | :--- | :--- |
| **Đúng** | `{PostId vừa tạo}` | Trả về `message: "Post deleted successfully"`, Status 200 OK. |
| **Sai (Post ID giả)** | `123` | Trả về lỗi không tìm thấy bài viết để xóa, Status 500. |

---

## 5. GET /api/page/post/{postId}/comments (Lấy bình luận)
| Case | Input `postId` | Mong đợi (Expected Result) |
| :--- | :--- | :--- |
| **Đúng** | `{PostId có comment}` | Trả về danh sách comment, Status 200 OK. |
| **Sai (ID sai)** | `wrong_id` | Trả về lỗi Facebook API, Status 500. |

---

## 6. GET /api/page/post/{postId}/likes (Lấy lượt Like)
| Case | Input `postId` | Mong đợi (Expected Result) |
| :--- | :--- | :--- |
| **Đúng** | `{PostId}` | Trả về danh sách người like hoặc count, Status 200 OK. |
| **Sai (ID sai)** | `unknown` | Lỗi API, Status 500. |

---

## 7. GET /api/page/{pageId}/insights (Lấy thống kê)
| Case | Input `pageId` | Mong đợi (Expected Result) |
| :--- | :--- | :--- |
| **Đúng** | `{PageId}` | Trả về mảng thống kê (mặc định lấy `page_views_total`), Status 200 OK. |
| **Sai (Không đủ quyền)** | Token thiếu quyền | Trả về lỗi ` (#10) This endpoint requires...`, Status 500. |

---

### Mẹo khi test trên Swagger:
1. Nhấn nút **"Try it out"**.
2. Nhập các giá trị vào ô tham số.
3. Nhấn **"Execute"**.
4. Quan sát phần **"Server response"** để chụp ảnh màn hình (mã code và nội dung JSON).

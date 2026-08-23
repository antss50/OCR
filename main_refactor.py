import os
import re
import sys
import unicodedata
from dataclasses import dataclass
from typing import Dict, List, Optional, Tuple

import cv2
import numpy as np
import pandas as pd
from paddleocr import PaddleOCR
from rapidfuzz import fuzz


# ============================================================
# CONFIGURATION
# ============================================================

DOCUMENT_TYPES = {
    "CONTRACT": "Hợp đồng",
    "CCCD_FRONT": "CCCD mặt trước",
    "CCCD_BACK": "CCCD mặt sau",
    "BANK_TRANSFER": "Chuyển khoản",
    "UNKNOWN": "Không xác định",
}


# Các field đúng theo Flow xử lý ảnh.
# Có thể thêm alias mà không cần sửa extractor.
TARGET_FIELDS = {
    # ------------------------- CONTRACT -------------------------
    "Cong_Tac_Vien_Ho_Ten": [
        "Cộng Tác Viên",
        "Cộng tác viên",
        "Cộng Tác Viên (Họ và Tên)",
        "Họ và tên cộng tác viên",
        "Họ và tên",
    ],
    "Dia_Chi": [
        "Địa chỉ",
        "Địa chỉ liên hệ",
        "Address",
    ],
    "Dien_Thoai": [
        "Điện thoại",
        "Số điện thoại",
        "Điện thoại liên hệ",
        "Phone",
        "Telephone",
    ],
    "So_CCCD": [
        "Số CCCD/CC",
        "Số CCCD / CC",
        "Số CCCD",
        "Số CMND",
        "Số / No.",
        "Số/No.",
        "CCCD",
        "CMND",
        "Số định danh cá nhân",
        "Personal identification number",
    ],
    "STK": [
        "Tài khoản ngân hàng số",
        "Tài khoản ngân hàng",
        "Số tài khoản ngân hàng",
        "STK",
        "Account number",
        "Bank account number",
    ],
    "Ngan_Hang": [
        "Tên ngân hàng",
        "Ngân hàng",
        "Ngân hàng nhận",
        "Ngân hàng thụ hưởng",
        "Đến ngân hàng",
        "Bank",
    ],
    "Chi_Nhanh": [
        "Chi nhánh",
        "Chi nhánh ngân hàng",
        "Bank branch",
        "Branch",
    ],
    "Thuoc_Nha_Thuoc": [
        "Thuộc Nhà Thuốc",
        "Thuộc nhà thuốc",
        "Nhà thuốc",
        "Belongs to Pharmacy",
    ],
    "So_Hop_Dong_ID": [
        "Số",
        "Số:",
        "ID",
        "Mã hợp đồng",
        "Số hợp đồng",
    ],
    "Mau": [
        "Mẫu",
        "Mẫu số",
        "Template",
    ],
    "Muc_Phi_Toi_Da": [
        "Mức phí tối đa nhà thuốc nhận được",
        "Mức phí tối đa nhà thuốc nhận được (gồm thuế thu nhập cá nhân nếu có)",
        "Mức phí tối đa",
        "Tổng tiền",
    ],

    # --------------------------- CCCD ---------------------------
    "CCCD_So": [
        "Số / No.",
        "Số/No.",
        "Số CCCD",
        "Số CCCD/CC",
        "Số định danh cá nhân",
        "Personal identification number",
        "CCCD",
    ],
    "CCCD_Ho_Ten": [
        "Họ và tên / Full name",
        "Họ và tên",
        "Họ tên",
        "Full name",
    ],
    "CCCD_Ngay_Cap": [
        "Ngày, tháng, năm / Date, month, year",
        "Ngày, tháng, năm",
        "Date, month, year",
        "Ngày, tháng, năm cấp",
        "Ngày cấp",
    ],
    "CCCD_Noi_Cap": [
        "Cục Cảnh sát Quản lý hành chính về trật tự xã hội",
        "Cục Trưởng cục Cảnh sát Quản lý hành chính về trật tự xã hội",
        "Bộ công an",
        "CỤC TRƯỞNG CỤC CẢNH SÁT ĐKQL CƯ TRÚ VÀ ĐLQG VỀ DÂN CƯ",
        "Cơ quan cấp CCCD",
    ],

    # ---------------------- BANK TRANSFER -----------------------
    "Transfer_Ngan_Hang": [
        "Tên ngân hàng",
        "Ngân hàng",
        "Ngân hàng nhận",
        "Ngân hàng thụ hưởng",
        "Đến ngân hàng",
        "Bank",
    ],
    "Transfer_STK_Nguoi_Nhan": [
        "Số tài khoản người nhận",
        "STK người nhận",
        "Tài khoản người nhận",
        "Tài khoản thụ hưởng",
        "Số tài khoản thụ hưởng",
        "Số TK nhận",
        "Số tài khoản nhận",
        "Account number",
    ],
    "Transfer_Ten_Nguoi_Nhan": [
        "Tên người nhận",
        "Người nhận",
        "Tên người thụ hưởng",
        "Người thụ hưởng",
        "Beneficiary",
        "Receiver",
    ],
}


# Các field dùng để xác định một element có phải label khác hay không.
ALL_ALIASES = [alias for aliases in TARGET_FIELDS.values() for alias in aliases]


@dataclass
class KeywordMatch:
    element: dict
    alias: str
    similarity: float
    score: float


# ============================================================
# TEXT UTILITIES
# ============================================================


def normalize_output_text(text: str) -> str:
    """Giữ nguyên dấu; chỉ làm sạch khoảng trắng/punctuation ngoài cùng."""
    if text is None:
        return ""
    text = re.sub(r"\s+", " ", str(text)).strip()
    return text.strip(" :;,-–—|.")


def normalize_match_text(text: str) -> str:
    """Dạng so sánh không dấu. KHÔNG dùng hàm này để xuất kết quả."""
    if text is None:
        return ""
    text = unicodedata.normalize("NFD", str(text).lower())
    text = "".join(ch for ch in text if unicodedata.category(ch) != "Mn")
    text = text.replace("đ", "d")
    text = text.replace("Đ", "d")
    text = re.sub(r"[^a-z0-9]+", " ", text)
    return re.sub(r"\s+", " ", text).strip()


def digits_only(text: str) -> str:
    return re.sub(r"\D", "", str(text or ""))


def valid_date(value: str) -> bool:
    m = re.fullmatch(r"(\d{1,2})[/-](\d{1,2})[/-](\d{4})", value.strip())
    if not m:
        return False
    d, month, year = map(int, m.groups())
    return 1 <= d <= 31 and 1 <= month <= 12 and 1900 <= year <= 2100


def valid_phone(value: str) -> bool:
    d = digits_only(value)
    return len(d) in (9, 10, 11) and d.startswith(("0", "84"))


def valid_cccd(value: str) -> bool:
    d = digits_only(value)
    return len(d) in (9, 12)


def valid_account(value: str) -> bool:
    d = digits_only(value)
    return 8 <= len(d) <= 16


# ============================================================
# TRANSACTION EXTRACTOR
# ============================================================

class TransactionPartyExtractor:
    """Layout-independent receiver extraction for transfer screenshots."""

    RECEIVER_KEYWORDS = (
        "chuyển đến", "đến tài khoản", "tài khoản nhận", "tài khoản thẻ nhận",
        "người nhận", "tên người nhận", "to account", "tới tài khoản",
        "tài khoản đích", "chuyển tiền tới", "tài khoản thụ hưởng",
        "số TK nhận", "số tài khoản thụ hưởng", "đến ngân hàng",
    )

    END_KEYWORDS = (
        "số tiền", "nội dung", "phí chuyển tiền", "thời gian", "mã giao dịch",
        "amount", "transaction id", "phí giao dịch",
    )

    BANK_NAMES = (
        "vietcombank", "vietinbank", "bidv", "techcombank", "mb bank", "mbbank",
        "acb", "sacombank", "tpbank", "vpbank", "hdbank", "vib", "ocb",
        "shb", "msb", "agribank", "seabank", "eximbank", "nam a bank",
        "bac a bank", "pvcombank", "cake", "uob", "standard chartered",
    )

    NAME_NOISE = (
        "chuyen khoan", "chuyen tien", "money chat", "napas", "thong tin",
        "tai khoan", "ngan hang", "mien phi", "quy khach", "so tien",
        "noi dung", "ma giao dich", "phi giao dich", "bank",
    )

    @classmethod
    def _matches(cls, text, keywords):
        normalized = normalize_match_text(text)
        return any(
            normalize_match_text(k) in normalized
            or fuzz.partial_ratio(normalize_match_text(k), normalized) >= 90
            for k in keywords
        )

    @classmethod
    def _extract_account_number(cls, elements):
        best = ""
        for element in elements:
            raw = str(element.get("text", ""))
            for candidate in re.findall(r"(?<!\d)(?:\d[\s.-]?){8,16}(?!\d)", raw):
                number = digits_only(candidate)
                if not (8 <= len(number) <= 16):
                    continue
                if len(number) == 8 and 1900 <= int(number[:4]) <= 2100:
                    continue
                if len(number) > len(best):
                    best = number
        return best

    @classmethod
    def _extract_person_name(cls, elements):
        candidates = []
        for position, element in enumerate(elements):
            raw = normalize_output_text(element.get("text", ""))
            folded = normalize_match_text(raw)
            words = folded.split()
            if not (2 <= len(words) <= 7):
                continue
            if any(ch.isdigit() for ch in raw):
                continue
            if any(noise in folded for noise in cls.NAME_NOISE):
                continue
            if not all(word.isalpha() for word in words):
                continue
            score = float(element.get("score", 0.0)) * 100
            score += 12 if raw.isupper() else 0
            score -= position * 0.5
            candidates.append((score, raw))
        return max(candidates, default=(0, ""))[1]

    def parse_parties(self, ocr_results):
        receiver = []
        scope = False
        for element in ocr_results:
            text = element.get("text", "")
            if self._matches(text, self.RECEIVER_KEYWORDS):
                scope = True
                continue
            if self._matches(text, self.END_KEYWORDS):
                scope = False
                continue
            if scope:
                receiver.append(element)

        return {
            "STK Người Nhận": self._extract_account_number(receiver),
            "Tên Người Nhận": self._extract_person_name(receiver),
        }


# ============================================================
# MAIN OCR EXTRACTOR
# ============================================================

class SpatialDocumentExtractor:
    """OCR + spatial extraction + document-specific rules.

    Public APIs giữ tương thích với code cũ:
      - extract_key_value(...)
      - batch_process_to_excel(...)

    Điểm khác biệt chính:
      1. matching không dấu nhưng output giữ nguyên text OCR;
      2. phân loại Contract / CCCD front / CCCD back / Transfer;
      3. OCR lại vùng value để cải thiện chữ tiếng Việt;
      4. rule riêng theo Flow, tránh dùng một rule generic cho mọi layout;
      5. validation theo loại field.
    """

    def __init__(
        self,
        lang="vi",
        use_gpu=False,
        min_ocr_score=0.45,
        keyword_similarity_threshold=80,
        ocr_scale=2.0,
        enhance_for_ocr=True,
        value_reocr=True,
        value_reocr_scale=2.5,
    ):
        print("[INFO] Đang tải mô hình OCR...")
        device = "gpu" if use_gpu else "cpu"
        self.ocr = PaddleOCR(
            lang=lang,
            device=device,
            use_textline_orientation=True,
            enable_mkldnn=False,
            enable_hpi=False,
        )
        self.min_ocr_score = float(min_ocr_score)
        self.keyword_similarity_threshold = float(keyword_similarity_threshold)
        self.ocr_scale = max(1.0, float(ocr_scale))
        self.enhance_for_ocr = bool(enhance_for_ocr)
        self.value_reocr = bool(value_reocr)
        self.value_reocr_scale = max(1.0, float(value_reocr_scale))
        self.transaction_party_extractor = TransactionPartyExtractor()
        print("[INFO] Tải mô hình OCR thành công!")

    # --------------------------------------------------------
    # PREPROCESSING
    # --------------------------------------------------------

    def _resize(self, image, scale):
        if image is None or image.size == 0 or scale <= 1:
            return image
        h, w = image.shape[:2]
        return cv2.resize(
            image,
            (int(round(w * scale)), int(round(h * scale))),
            interpolation=cv2.INTER_CUBIC,
        )

    def _preprocess_for_ocr(self, image):
        image = self._resize(image, self.ocr_scale)
        if image is None or not self.enhance_for_ocr:
            return image

        # Không sharpen quá mạnh vì dấu tiếng Việt rất nhỏ.
        lab = cv2.cvtColor(image, cv2.COLOR_BGR2LAB)
        l, a, b = cv2.split(lab)
        clahe = cv2.createCLAHE(clipLimit=1.6, tileGridSize=(8, 8))
        l = clahe.apply(l)
        enhanced = cv2.cvtColor(cv2.merge((l, a, b)), cv2.COLOR_LAB2BGR)

        # Sharpen nhẹ hơn bản cũ.
        blurred = cv2.GaussianBlur(enhanced, (0, 0), 0.8)
        return cv2.addWeighted(enhanced, 1.08, blurred, -0.08, 0)

    def _preprocess_value_crop(self, crop):
        crop = self._resize(crop, self.value_reocr_scale)
        if crop is None or crop.size == 0:
            return crop

        lab = cv2.cvtColor(crop, cv2.COLOR_BGR2LAB)
        l, a, b = cv2.split(lab)
        clahe = cv2.createCLAHE(clipLimit=1.4, tileGridSize=(8, 8))
        l = clahe.apply(l)
        enhanced = cv2.cvtColor(cv2.merge((l, a, b)), cv2.COLOR_LAB2BGR)
        return enhanced

    # --------------------------------------------------------
    # NORMALIZATION
    # --------------------------------------------------------

    @staticmethod
    def normalize_text(text):
        """Backward-compatible alias: normalization dùng cho matching."""
        return normalize_match_text(text)

    @staticmethod
    def clean_value_text(text):
        return normalize_output_text(text)

    @staticmethod
    def is_meaningful_text(text):
        if not text:
            return False
        return bool(str(text).strip(" :;,-–—|./"))

    # --------------------------------------------------------
    # BOX HELPERS
    # --------------------------------------------------------

    @staticmethod
    def _normalize_box(box):
        points = np.asarray(box)
        if points.shape != (4, 2):
            points = points.reshape(-1, 2)
        xs = points[:, 0]
        ys = points[:, 1]
        x1, x2 = float(xs.min()), float(xs.max())
        y1, y2 = float(ys.min()), float(ys.max())
        return {
            "x1": x1, "y1": y1, "x2": x2, "y2": y2,
            "w": max(0.0, x2 - x1), "h": max(0.0, y2 - y1),
            "cx": (x1 + x2) / 2, "cy": (y1 + y2) / 2,
        }

    @staticmethod
    def _vertical_overlap_ratio(a, b):
        overlap = max(0.0, min(a["y2"], b["y2"]) - max(a["y1"], b["y1"]))
        base = min(a["h"], b["h"])
        return overlap / base if base > 0 else 0.0

    @staticmethod
    def _horizontal_overlap_ratio(a, b):
        overlap = max(0.0, min(a["x2"], b["x2"]) - max(a["x1"], b["x1"]))
        base = min(a["w"], b["w"])
        return overlap / base if base > 0 else 0.0

    @classmethod
    def _is_same_row(cls, a, b, tolerance=0.35):
        overlap = cls._vertical_overlap_ratio(a, b)
        center_distance = abs(a["cy"] - b["cy"])
        max_height = max(a["h"], b["h"])
        if max_height <= 0:
            return False
        return overlap >= tolerance or center_distance <= max_height * 0.60

    @staticmethod
    def _horizontal_gap(a, b):
        if b["x1"] >= a["x2"]:
            return b["x1"] - a["x2"]
        if a["x1"] >= b["x2"]:
            return a["x1"] - b["x2"]
        return 0.0

    @staticmethod
    def _vertical_gap(a, b):
        if b["y1"] >= a["y2"]:
            return b["y1"] - a["y2"]
        if a["y1"] >= b["y2"]:
            return a["y1"] - b["y2"]
        return 0.0

    # --------------------------------------------------------
    # OCR PARSER
    # --------------------------------------------------------

    def _parse_ocr_result(self, ocr_results):
        elements = []
        if not ocr_results:
            return elements

        for page_result in ocr_results:
            try:
                data = page_result.json
                if isinstance(data, str):
                    import json
                    data = json.loads(data)
                res = data.get("res", data) if isinstance(data, dict) else {}
            except Exception as exc:
                print(f"[WARNING] Không đọc được OCR result: {exc}")
                continue

            texts = res.get("rec_texts", [])
            scores = res.get("rec_scores", [])
            polys = res.get("rec_polys", [])
            boxes = res.get("rec_boxes")
            count = min(len(texts), len(scores), len(polys) if len(polys) else len(boxes or []))

            for i in range(count):
                text = str(texts[i]).strip()
                score = float(scores[i])
                if not text or score < self.min_ocr_score:
                    continue

                try:
                    box = self._normalize_box(polys[i]) if len(polys) else None
                except Exception:
                    box = None

                if box is None and boxes is not None:
                    try:
                        x1, y1, x2, y2 = boxes[i]
                        box = {
                            "x1": float(x1), "y1": float(y1),
                            "x2": float(x2), "y2": float(y2),
                            "w": float(x2 - x1), "h": float(y2 - y1),
                            "cx": float((x1 + x2) / 2), "cy": float((y1 + y2) / 2),
                        }
                    except Exception:
                        box = None

                if box is None:
                    continue

                elements.append({
                    "text": text,  # luôn giữ text OCR gốc để output
                    "normalized_text": normalize_match_text(text),
                    "box": box,
                    "score": score,
                })

        elements.sort(key=lambda e: (e["box"]["y1"], e["box"]["x1"]))
        return elements

    def _run_ocr(self, image):
        try:
            return self._parse_ocr_result(self.ocr.predict(image))
        except Exception as exc:
            print(f"[ERROR] OCR thất bại: {exc}")
            return []

    # --------------------------------------------------------
    # DOCUMENT TYPE
    # --------------------------------------------------------

    def _count_keyword_hits(self, elements, keywords):
        hits = 0
        for elem in elements:
            text = elem["text"]
            if any(
                normalize_match_text(k) in normalize_match_text(text)
                or fuzz.partial_ratio(normalize_match_text(k), normalize_match_text(text)) >= 88
                for k in keywords
            ):
                hits += 1
        return hits

    def _detect_document_type(self, elements):
        scores = {
            "CONTRACT": 0,
            "CCCD_FRONT": 0,
            "CCCD_BACK": 0,
            "BANK_TRANSFER": 0,
        }

        scores["CONTRACT"] += self._count_keyword_hits(elements, (
            "cộng tác viên", "thuộc nhà thuốc", "mức phí tối đa nhà thuốc nhận được",
            "hợp đồng", "mẫu 2026", "tài khoản ngân hàng số",
        )) * 3

        scores["CCCD_FRONT"] += self._count_keyword_hits(elements, (
            "số / no.", "họ và tên / full name", "personal identification number",
            "ngày sinh / date of birth", "date of birth", "quốc tịch / nationality",
        )) * 3

        scores["CCCD_BACK"] += self._count_keyword_hits(elements, (
            "ngày, tháng, năm", "date, month, year", "cục cảnh sát",
            "quản lý hành chính", "fingerprint", "đặc điểm nhận dạng",
        )) * 3

        scores["BANK_TRANSFER"] += self._count_keyword_hits(elements, (
            "chuyển đến", "người nhận", "tài khoản thụ hưởng", "mã giao dịch",
            "transaction id", "nội dung", "số tiền", "đến ngân hàng",
        )) * 2

        best_type = max(scores, key=scores.get)
        if scores[best_type] <= 0:
            return "UNKNOWN", scores
        return best_type, scores

    # --------------------------------------------------------
    # KEYWORD MATCHING
    # --------------------------------------------------------

    def _keyword_similarity(self, alias, text):
        a = normalize_match_text(alias)
        b = normalize_match_text(text)
        if not a or not b:
            return 0.0
        if a == b:
            return 100.0
        if a in b:
            return 98.0
        return max(fuzz.partial_ratio(a, b), fuzz.ratio(a, b))

    def _find_best_keyword(self, elements, aliases):
        best = None
        for elem in elements:
            for alias in aliases:
                alias_norm = normalize_match_text(alias)
                if len(alias_norm) < 4:
                    continue
                similarity = self._keyword_similarity(alias, elem["text"])
                if similarity < self.keyword_similarity_threshold:
                    continue
                final_score = (
                    similarity * 0.70
                    + elem["score"] * 100 * 0.20
                    + min(len(alias_norm) / 30.0, 1.0) * 100 * 0.10
                )
                candidate = KeywordMatch(elem, alias, similarity, final_score)
                if best is None or candidate.score > best.score:
                    best = candidate
        return best

    def _is_another_keyword(self, elem, aliases, exclude_aliases=None):
        exclude_aliases = set(exclude_aliases or [])
        for alias in aliases:
            if alias in exclude_aliases:
                continue
            if len(normalize_match_text(alias)) < 4:
                continue
            if self._keyword_similarity(alias, elem["text"]) >= 89:
                return True
        return False

    # --------------------------------------------------------
    # SPATIAL VALUE EXTRACTION
    # --------------------------------------------------------

    def _extract_inline_value(self, text, alias):
        if not text:
            return ""
        # exact alias first; OCR may have lost accents so only use inline slicing
        # when the alias text is actually present in raw OCR.
        match = re.search(re.escape(alias), text, re.IGNORECASE)
        if not match:
            return ""
        return normalize_output_text(text[match.end():])

    def _find_right_candidates(self, key_elem, elements, image_width, max_width_ratio=0.45):
        key = key_elem["box"]
        candidates = []
        max_distance = image_width * max_width_ratio
        for elem in elements:
            if elem is key_elem:
                continue
            box = elem["box"]
            if box["x1"] < key["x2"] - 2:
                continue
            if not self._is_same_row(key, box):
                continue
            if self._horizontal_gap(key, box) > max_distance:
                continue
            if not self.is_meaningful_text(elem["text"]):
                continue
            candidates.append(elem)
        candidates.sort(key=lambda e: self._horizontal_gap(key, e["box"]))
        return candidates

    def _find_bottom_candidates(self, key_elem, elements):
        key = key_elem["box"]
        max_y = max(key["h"] * 6, 120)
        candidates = []
        for elem in elements:
            if elem is key_elem:
                continue
            box = elem["box"]
            if box["y1"] < key["y2"] - 2:
                continue
            if self._vertical_gap(key, box) > max_y:
                continue
            if abs(box["cx"] - key["cx"]) > max(key["w"] * 2.0, 160):
                continue
            if not self.is_meaningful_text(elem["text"]):
                continue
            candidates.append(elem)
        candidates.sort(key=lambda e: self._vertical_gap(key, e["box"]))
        return candidates

    def _rank_candidates(self, key_elem, candidates, prefer_right=True):
        key = key_elem["box"]
        ranked = []
        for elem in candidates:
            box = elem["box"]
            dx = self._horizontal_gap(key, box)
            dy = self._vertical_gap(key, box)
            same_row = self._is_same_row(key, box)
            score = elem["score"] * 40
            score += 45 if same_row else 0
            score += 12 if prefer_right and same_row else 0
            score -= min(dx / 12, 20)
            score -= min(dy / 12, 20)
            if len(normalize_output_text(elem["text"])) >= 2:
                score += 8
            ranked.append((score, elem))
        ranked.sort(key=lambda x: x[0], reverse=True)
        return ranked

    def _collect_value_elements(self, key_elem, candidates, all_aliases, same_row_only=False):
        if not candidates:
            return []
        selected = []
        for elem in candidates:
            if self._is_another_keyword(elem, all_aliases):
                if selected:
                    break
                continue
            if not self.is_meaningful_text(elem["text"]):
                continue
            if selected:
                last = selected[-1]
                gap_x = self._horizontal_gap(last["box"], elem["box"])
                gap_y = self._vertical_gap(last["box"], elem["box"])
                max_x = max(last["box"]["h"] * 9, 130)
                max_y = max(last["box"]["h"] * 2.8, 60)
                if same_row_only and not self._is_same_row(last["box"], elem["box"]):
                    break
                if gap_x > max_x or gap_y > max_y:
                    break
            selected.append(elem)
        return selected

    def _build_value(self, elements):
        if not elements:
            return ""
        rows = []
        for elem in sorted(elements, key=lambda e: (e["box"]["cy"], e["box"]["x1"])):
            placed = False
            for row in rows:
                if self._is_same_row(row[0]["box"], elem["box"]):
                    row.append(elem)
                    placed = True
                    break
            if not placed:
                rows.append([elem])
        for row in rows:
            row.sort(key=lambda e: e["box"]["x1"])
        rows.sort(key=lambda row: min(e["box"]["y1"] for e in row))
        lines = []
        for row in rows:
            line = " ".join(
                normalize_output_text(e["text"])
                for e in row
                if normalize_output_text(e["text"])
            )
            if line:
                lines.append(line)
        return " ".join(lines).strip()

    # --------------------------------------------------------
    # CROP + VALUE RE-OCR
    # --------------------------------------------------------

    @staticmethod
    def _crop_box(image, elements, padding=8):
        if not elements:
            return None
        x1 = max(0, int(min(e["box"]["x1"] for e in elements)) - padding)
        y1 = max(0, int(min(e["box"]["y1"] for e in elements)) - padding)
        x2 = min(image.shape[1], int(max(e["box"]["x2"] for e in elements)) + padding)
        y2 = min(image.shape[0], int(max(e["box"]["y2"] for e in elements)) + padding)
        if x2 <= x1 or y2 <= y1:
            return None
        return image[y1:y2, x1:x2]

    @staticmethod
    def _save_crop(crop, path):
        if crop is None or crop.size == 0:
            return False
        os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
        ext = os.path.splitext(path)[1] or ".png"
        ok, encoded = cv2.imencode(ext, crop)
        if not ok:
            return False
        encoded.tofile(path)
        return True

    def _reocr_crop(self, crop, field_name):
        """OCR lại value crop; chỉ thay output nếu kết quả hợp lý hơn."""
        if not self.value_reocr or crop is None or crop.size == 0:
            return "", 0.0

        variants = [crop]
        enhanced = self._preprocess_value_crop(crop)
        if enhanced is not None and enhanced.size:
            variants.append(enhanced)

        candidates = []
        for variant in variants:
            elements = self._run_ocr(variant)
            if not elements:
                continue
            text = self._build_value(elements)
            if not text:
                continue
            score = float(np.mean([e["score"] for e in elements]))
            candidates.append((score, text))

        if not candidates:
            return "", 0.0

        # Field validation ưu tiên text có pattern đúng.
        def quality(item):
            score, text = item
            bonus = 0.0
            if field_name in {"So_CCCD", "CCCD_So"} and valid_cccd(text):
                bonus += 30
            if field_name in {"Dien_Thoai"} and valid_phone(text):
                bonus += 25
            if field_name in {"STK", "Transfer_STK_Nguoi_Nhan"} and valid_account(text):
                bonus += 25
            if field_name == "CCCD_Ngay_Cap" and valid_date(text):
                bonus += 30
            if len(text) >= 2:
                bonus += 5
            return score * 100 + bonus

        return max(candidates, key=quality)

    # --------------------------------------------------------
    # FIELD VALIDATION / NORMALIZATION
    # --------------------------------------------------------

    def _validate_field(self, field_name, text):
        text = normalize_output_text(text)
        if not text:
            return ""

        if field_name in {"So_CCCD", "CCCD_So"}:
            m = re.search(r"(?<!\d)(\d{12}|\d{9})(?!\d)", text)
            return m.group(1) if m else ""

        if field_name == "Dien_Thoai":
            candidates = re.findall(r"(?:\+?84|0)[\d .-]{8,13}", text)
            for candidate in candidates:
                d = digits_only(candidate)
                if valid_phone(d):
                    return d
            return ""

        if field_name in {"STK", "Transfer_STK_Nguoi_Nhan"}:
            for candidate in re.findall(r"(?<!\d)(?:\d[\s.-]?){8,16}(?!\d)", text):
                d = digits_only(candidate)
                if valid_account(d):
                    return d
            return ""

        if field_name == "CCCD_Ngay_Cap":
            m = re.search(r"(?<!\d)(\d{1,2}[/-]\d{1,2}[/-]\d{4})(?!\d)", text)
            return m.group(1) if m and valid_date(m.group(1)) else ""

        if field_name == "Muc_Phi_Toi_Da":
            # Giữ nguyên format OCR để không làm mất đơn vị tiền; nếu có số thì lấy cụm tiền rõ nhất.
            matches = re.findall(r"(?:\d[\d., ]{2,})(?:\s*(?:VNĐ|VND|đ|dong|đồng))?", text, re.I)
            if matches:
                return normalize_output_text(matches[-1])

        return text

    # --------------------------------------------------------
    # DIRECT FIELD RULES
    # --------------------------------------------------------

    def _find_value_right_of_label(self, field_name, aliases, elements, image_width, all_aliases):
        match = self._find_best_keyword(elements, aliases)
        if not match:
            return None
        key = match.element

        inline = self._extract_inline_value(key["text"], match.alias)
        if inline:
            return inline, [key], match

        candidates = self._find_right_candidates(key, elements, image_width)
        candidates = [e for e in candidates if not self._is_another_keyword(e, all_aliases)]
        ranked = self._rank_candidates(key, candidates, prefer_right=True)
        if not ranked:
            return None
        selected = self._collect_value_elements(key, [e for _, e in ranked], all_aliases, same_row_only=True)
        if not selected:
            return None
        value = self._build_value(selected)
        return value, selected, match

    def _find_value_below_label(self, aliases, elements, all_aliases):
        match = self._find_best_keyword(elements, aliases)
        if not match:
            return None
        key = match.element
        candidates = self._find_bottom_candidates(key, elements)
        candidates = [e for e in candidates if not self._is_another_keyword(e, all_aliases)]
        ranked = self._rank_candidates(key, candidates, prefer_right=False)
        if not ranked:
            return None
        selected = self._collect_value_elements(key, [e for _, e in ranked], all_aliases)
        if not selected:
            return None
        value = self._build_value(selected)
        return value, selected, match

    def _extract_cccd_front(self, field_name, elements, image):
        if field_name == "CCCD_So":
            match = self._find_best_keyword(elements, TARGET_FIELDS[field_name])
            if not match:
                return None
            # Số có thể nằm cùng box hoặc ngay bên phải.
            m = re.search(r"(?<!\d)(\d{12}|\d{9})(?!\d)", match.element["text"])
            if m:
                return m.group(1), [match.element], match
            candidates = self._find_right_candidates(match.element, elements, image.shape[1], 0.5)
            for elem in candidates:
                m = re.search(r"(?<!\d)(\d{12}|\d{9})(?!\d)", elem["text"])
                if m:
                    return m.group(1), [elem], match
            return None

        if field_name == "CCCD_Ho_Ten":
            return self._find_value_below_label(TARGET_FIELDS[field_name], elements, ALL_ALIASES)

        return None

    def _extract_cccd_back(self, field_name, elements, image):
        if field_name == "CCCD_Ngay_Cap":
            match = self._find_best_keyword(elements, TARGET_FIELDS[field_name])
            if match:
                m = re.search(r"(?<!\d)(\d{1,2}[/-]\d{1,2}[/-]\d{4})(?!\d)", match.element["text"])
                if m:
                    return m.group(1), [match.element], match
                below = self._find_bottom_candidates(match.element, elements)
                for elem in below:
                    m = re.search(r"(?<!\d)(\d{1,2}[/-]\d{1,2}[/-]\d{4})(?!\d)", elem["text"])
                    if m:
                        return m.group(1), [elem], match
            return None

        if field_name == "CCCD_Noi_Cap":
            ordered = sorted(elements, key=lambda e: (e["box"]["y1"], e["box"]["x1"]))
            for i, elem in enumerate(ordered):
                folded = normalize_match_text(elem["text"])
                if "cuc" not in folded or "canh sat" not in folded:
                    continue
                group = []
                for next_elem in ordered[i:i + 6]:
                    f = normalize_match_text(next_elem["text"])
                    if any(marker in f for marker in ("ngon tay", "finger", "dac diem nhan dang")):
                        break
                    group.append(next_elem)
                if group:
                    return self._build_value(group), group, KeywordMatch(elem, "Cơ quan cấp CCCD", 100, elem["score"] * 100)
            return None

        return None

    def _extract_contract_special(self, field_name, elements, image):
        if field_name == "So_Hop_Dong_ID":
            # Tìm cụm Số ... ID ..., ưu tiên text chứa ID.
            ordered = sorted(elements, key=lambda e: (e["box"]["y1"], e["box"]["x1"]))
            for i, elem in enumerate(ordered):
                folded = normalize_match_text(elem["text"])
                if "id" in folded or re.search(r"\b\d{2,}\b", elem["text"]):
                    window = ordered[max(0, i - 1):i + 3]
                    text = self._build_value(window)
                    if re.search(r"\d", text):
                        return text, window, KeywordMatch(elem, "Số / ID", 100, elem["score"] * 100)
            return None

        if field_name == "Mau":
            # Mẫu nằm góc trên phải theo Flow; lấy text gần góc trên phải có từ Mẫu.
            candidates = []
            for elem in elements:
                if self._keyword_similarity("Mẫu", elem["text"]) >= 80:
                    candidates.append(elem)
            if candidates:
                elem = max(candidates, key=lambda e: (e["box"]["x1"], -e["box"]["y1"]))
                inline = self._extract_inline_value(elem["text"], "Mẫu")
                if inline:
                    return inline, [elem], KeywordMatch(elem, "Mẫu", self._keyword_similarity("Mẫu", elem["text"]), elem["score"] * 100)
                right = self._find_right_candidates(elem, elements, image.shape[1], 0.35)
                if right:
                    value = self._build_value([right[0]])
                    if value:
                        return value, [right[0]], KeywordMatch(elem, "Mẫu", self._keyword_similarity("Mẫu", elem["text"]), elem["score"] * 100)
            return None

        if field_name == "Muc_Phi_Toi_Da":
            match = self._find_best_keyword(elements, TARGET_FIELDS[field_name])
            if not match:
                return None
            # Tiền thường nằm cùng dòng hoặc bên dưới label dài.
            candidates = self._find_right_candidates(match.element, elements, image.shape[1], 0.55)
            money = [e for e in candidates if re.search(r"\d", e["text"])]
            if money:
                e = money[0]
                return self._validate_field(field_name, e["text"]), [e], match
            below = self._find_bottom_candidates(match.element, elements)
            money = [e for e in below if re.search(r"\d", e["text"])]
            if money:
                e = money[0]
                return self._validate_field(field_name, e["text"]), [e], match
            return None

        return None

    # --------------------------------------------------------
    # FIELD EXTRACTION
    # --------------------------------------------------------

    def _result(self):
        return {
            "text": "",
            "crop_file": "",
            "score": 0.0,
            "keyword": "",
            "keyword_similarity": 0.0,
        }

    def _finish_result(self, result, field_name, value, elements, match, image, crop_dir, base_name):
        value = self._validate_field(field_name, value)
        if not value:
            return result

        # Re-OCR crop chỉ cho value có khả năng là text tự do.
        crop = self._crop_box(image, elements, padding=8)
        reocr_text, reocr_score = self._reocr_crop(crop, field_name)

        # Với text tự do, re-OCR có confidence cao hơn và không ngắn bất thường -> ưu tiên.
        if reocr_text and self._is_better_reocr(field_name, value, reocr_text, reocr_score):
            value = self._validate_field(field_name, reocr_text) or value

        result["text"] = value
        result["score"] = float(np.mean([e["score"] for e in elements])) if elements else 0.0
        result["keyword"] = match.element["text"] if match else ""
        result["keyword_similarity"] = match.similarity if match else 0.0

        crop_path = os.path.join(crop_dir, f"{base_name}_{field_name}.png")
        if self._save_crop(crop, crop_path):
            result["crop_file"] = crop_path
        return result

    def _is_better_reocr(self, field_name, original, reocr, reocr_score):
        if not reocr:
            return False
        if field_name in {"So_CCCD", "CCCD_So", "Dien_Thoai", "STK", "Transfer_STK_Nguoi_Nhan", "CCCD_Ngay_Cap"}:
            # Các field pattern phải được validate trước khi thay.
            return reocr_score >= 0.50
        if len(reocr) < 2:
            return False
        # Không thay bằng một chuỗi ngắn hơn quá nhiều.
        if len(reocr) < max(2, int(len(original) * 0.55)):
            return False
        return reocr_score >= 0.58

    def _extract_field(self, field_name, elements, image, doc_type, crop_dir, base_name):
        result = self._result()

        # 1) Rule riêng theo document.
        special = None
        if doc_type == "CCCD_FRONT" and field_name in {"CCCD_So", "CCCD_Ho_Ten"}:
            special = self._extract_cccd_front(field_name, elements, image)
        elif doc_type == "CCCD_BACK" and field_name in {"CCCD_Ngay_Cap", "CCCD_Noi_Cap"}:
            special = self._extract_cccd_back(field_name, elements, image)
        elif doc_type == "CONTRACT" and field_name in {"So_Hop_Dong_ID", "Mau", "Muc_Phi_Toi_Da"}:
            special = self._extract_contract_special(field_name, elements, image)

        if special:
            value, value_elements, match = special
            return self._finish_result(result, field_name, value, value_elements, match, image, crop_dir, base_name)

        # Không cho field của loại tài liệu khác tự do guess.
        allowed = {
            "CONTRACT": {"Cong_Tac_Vien_Ho_Ten", "Dia_Chi", "Dien_Thoai", "So_CCCD", "STK", "Ngan_Hang", "Chi_Nhanh", "Thuoc_Nha_Thuoc", "So_Hop_Dong_ID", "Mau", "Muc_Phi_Toi_Da"},
            "CCCD_FRONT": {"CCCD_So", "CCCD_Ho_Ten"},
            "CCCD_BACK": {"CCCD_Ngay_Cap", "CCCD_Noi_Cap"},
            "BANK_TRANSFER": {"Transfer_Ngan_Hang", "Transfer_STK_Nguoi_Nhan", "Transfer_Ten_Nguoi_Nhan"},
            "UNKNOWN": set(TARGET_FIELDS.keys()),
        }
        if field_name not in allowed.get(doc_type, set()):
            return result

        aliases = TARGET_FIELDS[field_name]
        match = self._find_best_keyword(elements, aliases)
        if not match:
            return result

        # Contract/transfer: Flow nói value ở bên phải label.
        prefer_right = doc_type in {"CONTRACT", "BANK_TRANSFER"}
        all_aliases = ALL_ALIASES

        inline = self._extract_inline_value(match.element["text"], match.alias)
        if inline:
            value = self._validate_field(field_name, inline)
            if value:
                return self._finish_result(result, field_name, value, [match.element], match, image, crop_dir, base_name)

        right = self._find_right_candidates(match.element, elements, image.shape[1], 0.55 if prefer_right else 0.40)
        right = [e for e in right if not self._is_another_keyword(e, all_aliases)]
        bottom = self._find_bottom_candidates(match.element, elements)
        bottom = [e for e in bottom if not self._is_another_keyword(e, all_aliases)]

        right_ranked = self._rank_candidates(match.element, right, prefer_right=True)
        bottom_ranked = self._rank_candidates(match.element, bottom, prefer_right=False)

        # Với hợp đồng: bắt buộc ưu tiên phải. CCCD name/date: ưu tiên dưới.
        if doc_type == "CONTRACT":
            ranked = right_ranked or bottom_ranked
            same_row = True
        elif doc_type == "CCCD_FRONT":
            ranked = bottom_ranked or right_ranked
            same_row = False
        elif doc_type == "CCCD_BACK":
            ranked = bottom_ranked or right_ranked
            same_row = False
        else:
            ranked = right_ranked or bottom_ranked
            same_row = False

        if not ranked:
            return result

        selected = self._collect_value_elements(
            match.element,
            [e for _, e in ranked],
            all_aliases,
            same_row_only=same_row,
        )
        if not selected:
            return result

        value = self._build_value(selected)
        value = self._validate_field(field_name, value)
        if not value:
            return result

        return self._finish_result(result, field_name, value, selected, match, image, crop_dir, base_name)

    # --------------------------------------------------------
    # PUBLIC API
    # --------------------------------------------------------

    def extract_key_value(
        self,
        image_path,
        target_keywords=TARGET_FIELDS,
        output_crop_dir="crops",
        include_transaction_parties=False,
    ):
        os.makedirs(output_crop_dir, exist_ok=True)

        try:
            image = cv2.imdecode(np.fromfile(image_path, dtype=np.uint8), cv2.IMREAD_COLOR)
        except OSError as exc:
            print(f"[ERROR] Không thể đọc ảnh: {image_path} ({exc})")
            return {}
        if image is None:
            print(f"[ERROR] Không thể đọc ảnh: {image_path}")
            return {}

        processed = self._preprocess_for_ocr(image)
        print(f"[INFO] OCR: {os.path.basename(image_path)}")
        elements = self._run_ocr(processed)
        if not elements:
            print("[WARNING] OCR không nhận được text.")
            return {}

        doc_type, doc_scores = self._detect_document_type(elements)
        print(f"[INFO] Document type: {DOCUMENT_TYPES.get(doc_type, doc_type)} | scores={doc_scores}")
        print(f"[INFO] OCR nhận được {len(elements)} text boxes.")

        # Transaction parser chỉ dùng cho transfer hoặc khi caller yêu cầu.
        transaction_parties = None
        if doc_type == "BANK_TRANSFER" or include_transaction_parties:
            transaction_parties = self.transaction_party_extractor.parse_parties(elements)

        result = {}
        base_name = os.path.splitext(os.path.basename(image_path))[0]

        for field_name in target_keywords:
            print(f"    [FIELD] {field_name}")
            field_result = self._extract_field(
                field_name=field_name,
                elements=elements,
                image=processed,
                doc_type=doc_type,
                crop_dir=output_crop_dir,
                base_name=base_name,
            )
            result[field_name] = field_result
            if field_result["text"]:
                print(f"        -> {field_result['text']}")
                print(f"        -> confidence: {field_result['score']:.3f}")
            else:
                print("        -> Không tìm thấy")

        if doc_type == "BANK_TRANSFER" and transaction_parties is not None:
            # Ưu tiên kết quả spatial field nếu có; fallback sang parser cũ.
            result["_transaction_parties"] = transaction_parties

        result["_document_type"] = doc_type
        return result

    def batch_process_to_excel(
        self,
        image_folder,
        target_keywords=TARGET_FIELDS,
        output_excel_path="KetQua_TrichXuat_Anh.xlsx",
        output_crop_dir="crops",
    ):
        if not os.path.isdir(image_folder):
            print(f"[ERROR] Folder không tồn tại: {image_folder}")
            return

        valid_extensions = (".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".tif", ".webp")
        image_files = sorted(
            f for f in os.listdir(image_folder)
            if f.lower().endswith(valid_extensions)
        )
        print(f"[INFO] Tìm thấy {len(image_files)} ảnh cần xử lý.")

        records = []
        for index, file_name in enumerate(image_files, 1):
            image_path = os.path.join(image_folder, file_name)
            print("\n" + "=" * 70)
            print(f"[{index}/{len(image_files)}] Đang xử lý: {file_name}")
            print("=" * 70)

            try:
                result = self.extract_key_value(
                    image_path=image_path,
                    target_keywords=target_keywords,
                    output_crop_dir=output_crop_dir,
                    include_transaction_parties=True,
                )
            except Exception as exc:
                print(f"[ERROR] Lỗi khi xử lý {file_name}: {exc}")
                result = {}

            row = {
                "File Name": file_name,
                "Document Type": DOCUMENT_TYPES.get(result.get("_document_type", "UNKNOWN"), "Không xác định"),
            }

            for field in target_keywords:
                value = result.get(field, {})
                if isinstance(value, dict):
                    row[field] = value.get("text", "")
                    row[f"Score_{field}"] = value.get("score", 0.0)
                    row[f"Keyword_{field}"] = value.get("keyword", "")
                    row[f"Keyword_Similarity_{field}"] = value.get("keyword_similarity", 0.0)
                    row[f"Crop_{field}"] = value.get("crop_file", "")
                else:
                    row[field] = ""
                    row[f"Score_{field}"] = 0.0
                    row[f"Keyword_{field}"] = ""
                    row[f"Keyword_Similarity_{field}"] = 0.0
                    row[f"Crop_{field}"] = ""

            parties = result.get("_transaction_parties", {})
            row["STK Người Nhận"] = parties.get("STK Người Nhận", "")
            row["Tên Người Nhận"] = parties.get("Tên Người Nhận", "")
            records.append(row)

        if not records:
            print("[WARNING] Không có dữ liệu để xuất.")
            return

        df = pd.DataFrame(records)
        try:
            df.to_excel(output_excel_path, index=False)
        except PermissionError:
            root, ext = os.path.splitext(output_excel_path)
            fallback = f"{root}_moi{ext}"
            df.to_excel(fallback, index=False)
            output_excel_path = fallback
            print(f"[WARNING] File Excel đang mở. Đã xuất file mới: {fallback}")

        print("\n" + "=" * 70)
        print("[HOÀN TẤT]")
        print(f"Dữ liệu đã được xuất ra: {output_excel_path}")
        print("=" * 70)


# ============================================================
# MAIN
# ============================================================

if __name__ == "__main__":
    IMAGE_DIR = "images"
    OUTPUT_EXCEL = "KetQua_TrichXuat_Anh.xlsx"
    CROP_DIR = "crops"

    os.makedirs(IMAGE_DIR, exist_ok=True)
    os.makedirs(CROP_DIR, exist_ok=True)

    extractor = SpatialDocumentExtractor(
        lang="vi",
        use_gpu=False,
        min_ocr_score=0.45,
        keyword_similarity_threshold=80,
        ocr_scale=2.0,
        enhance_for_ocr=True,
        value_reocr=True,
        value_reocr_scale=2.5,
    )

    extractor.batch_process_to_excel(
        image_folder=IMAGE_DIR,
        target_keywords=TARGET_FIELDS,
        output_excel_path=OUTPUT_EXCEL,
        output_crop_dir=CROP_DIR,
    )
import os
import sys
import re
import unicodedata
import cv2
import numpy as np
import pandas as pd

from paddleocr import PaddleOCR
from rapidfuzz import fuzz


class TransactionPartyExtractor:
    """
    Extract STK và Tên người nhận từ OCR result.
    1. Tìm các OCR element chứa keyword liên quan đến người nhận.
    2. Gom các element liên quan thành một nhóm.
    3. Tìm STK và Tên người nhận trong nhóm.
    4. Trả về kết quả.
    5. Nếu không tìm thấy, trả về chuỗi rỗng.
    """

    # SENDER_KEYWORDS = (
    #     "từ tài khoản", "tài khoản nguồn", "tài khoản trích",
    #     "người gửi", "from account",
    # )
    RECEIVER_KEYWORDS = (
        "chuyển đến","đến tài khoản", "tài khoản nhận", "tài khoản thẻ nhận",
        "người nhận", "tên người nhận", "to account", "tới tài khoản", "tài khoản đích",
        "chuyển tiền tới", "tài khoản thụ hưởng", "số TK nhận", "số tài khoản thụ hưởng",
        "đến ngân hàng"
    )
    END_KEYWORDS = (
        "số tiền", "nội dung", "phí chuyển tiền", "thời gian",
        "mã giao dịch", "amount", "transaction id",
    )
    NAME_NOISE = (
        "chuyen khoan", "chuyen tien", "vietcombank", "vietinbank",
        "bidv", "techcombank", "mbbank", "money chat", "napas",
        "thong tin", "tai khoan", "ngan hang", "mien phi", "quy khach",
        "so tien", "noi dung", "ma giao dich", "phi giao dich",
    )
    MONEY_MARKERS = ("vnd", "đồng", "dong", "vnđ", "₫")

    """
        Chuẩn hoá text để so sánh, chuyển về lower case, loại bỏ dấu tiếng Việt, ký tự đặc biệt, khoảng trắng thừa.
    """
    @staticmethod
    def _normalise(text):
        text = unicodedata.normalize("NFD", str(text).lower())
        text = "".join(ch for ch in text if unicodedata.category(ch) != "Mn")
        text = text.replace("đ", "d")
        return re.sub(r"[^a-z0-9]+", " ", text).strip()
    
    """
        Kiểm tra text có chứa keyword cần tìm hay không, sử dụng fuzzy matching.
    """
    @classmethod
    def _matches(cls, text, keywords):
        normalised = cls._normalise(text)
        for keyword in keywords:
            keyword_normalised = cls._normalise(keyword)
            if (keyword_normalised in normalised
                    or fuzz.ratio(keyword_normalised, normalised) >= 90):
                return True
        return False

    """
        Tìm STK trong các OCR element (8-16 số), bỏ qua các element có chứa từ liên quan đến tiền tệ hoặc ngày tháng năm (01-01-2026).
    """
    @classmethod
    def _extract_account_number(cls, elements):
        for element in elements:
            raw = str(element["text"])
            normalised = cls._normalise(raw)
            if any(marker in normalised or marker in raw.lower()
                   for marker in cls.MONEY_MARKERS):
                continue

            for candidate in re.findall(r"(?<!\d)(?:\d[\s.-]?){8,16}(?!\d)", raw):
                number = re.sub(r"\D", "", candidate)
                looks_like_date = (
                    len(number) == 8
                    and number[:4] in {str(year) for year in range(1900, 2101)}
                    and 1 <= int(number[4:6]) <= 12
                    and 1 <= int(number[6:8]) <= 31
                )
                if 8 <= len(number) <= 16 and not looks_like_date:
                    return number
        return ""

    """
        Tìm tên người nhận trong các OCR element, bỏ qua các element có chứa số, hoặc các từ liên quan đến ngân hàng, chuyển tiền, phí giao dịch, nội dung, thông tin.
    """
    @classmethod
    def _extract_person_name(cls, elements):
        best_name = ""
        best_score = float("-inf")
        for position, element in enumerate(elements):
            raw = re.sub(r"\s+", " ", str(element["text"])).strip()
            normalised = cls._normalise(raw)
            words = normalised.split()
            if (len(words) < 2 or len(words) > 6 or any(ch.isdigit() for ch in raw)
                    or any(noise in normalised for noise in cls.NAME_NOISE)):
                continue
            if not all(word.isalpha() for word in words):
                continue

            # Receipt names commonly uppercase; title case remains valid.
            score = (10 if raw.isupper() else 0) - position
            if score > best_score:
                best_name, best_score = raw, score
        return best_name

    """
        Parse OCR result để tìm thông tin người nhận.
    """
    def parse_parties(self, ocr_results):
        sender, receiver = [], []
        scope = None
        for element in ocr_results:
            text = element.get("text", "")
            # if self._matches(text, self.SENDER_KEYWORDS):
            #     scope = "sender"
            #     continue
            if self._matches(text, self.RECEIVER_KEYWORDS):
                scope = "receiver"
                continue
            if self._matches(text, self.END_KEYWORDS):
                scope = None
                continue
            # if scope == "sender":
            #     sender.append(element)
            elif scope == "receiver":
                receiver.append(element)

        return {
            # "STK Người Gửi": self._extract_account_number(sender),
            # "Tên Người Gửi": self._extract_person_name(sender),
            "STK Người Nhận": self._extract_account_number(receiver),
            "Tên Người Nhận": self._extract_person_name(receiver),
        }


# ============================================================
# UTF-8 OUTPUT
# ============================================================

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(
        encoding="utf-8",
        errors="replace"
    )


# ============================================================
# OCR DOCUMENT EXTRACTOR
# ============================================================

class SpatialDocumentExtractor:

    def __init__(
        self,
        lang="vi",
        use_gpu=False,
        min_ocr_score=0.50,
        keyword_similarity_threshold=82,
        ocr_scale=2.0,
        enhance_for_ocr=True
    ):
        """
        OCR extractor dùng PaddleOCR 3.x.

        Parameters
        ----------
        lang:
            Ngôn ngữ OCR.

        use_gpu:
            True nếu muốn sử dụng GPU.

        min_ocr_score:
            Confidence tối thiểu của OCR text.

        keyword_similarity_threshold:
            Ngưỡng fuzzy matching keyword.
        """

        print("[INFO] Đang tải mô hình OCR...")

        device = "gpu" if use_gpu else "cpu"

        self.ocr = PaddleOCR(
            lang=lang,
            device=device,
            use_textline_orientation=True,
            enable_mkldnn=False,
            enable_hpi=False,
        )

        self.min_ocr_score = min_ocr_score
        self.keyword_similarity_threshold = keyword_similarity_threshold
        self.transaction_party_extractor = TransactionPartyExtractor()
        self.ocr_scale = max(1.0, float(ocr_scale))
        self.enhance_for_ocr = bool(enhance_for_ocr)

        print("[INFO] Tải mô hình OCR thành công!")


    # ========================================================
    # IMAGE PREPROCESSING
    # ========================================================

    def _resize_for_ocr(self, image, scale=None):
        """Resize ảnh theo scale OCR, giữ nguyên BGR."""
        if image is None or image.size == 0:
            return image

        scale = self.ocr_scale if scale is None else max(1.0, float(scale))
        if scale <= 1.0:
            return image.copy()

        height, width = image.shape[:2]
        return cv2.resize(
            image,
            (int(round(width * scale)), int(round(height * scale))),
            interpolation=cv2.INTER_CUBIC,
        )

    def _preprocess_for_ocr(self, image):
        """Upscale and enhance a raster image without discarding colour data."""
        if image is None or image.size == 0:
            return image

        processed = self._resize_for_ocr(image)

        if not self.enhance_for_ocr:
            return processed

        # CLAHE works on luminance; keep BGR colour channels for PaddleOCR.
        lab = cv2.cvtColor(processed, cv2.COLOR_BGR2LAB)
        lightness, channel_a, channel_b = cv2.split(lab)
        clahe = cv2.createCLAHE(clipLimit=1.5, tileGridSize=(8, 8))
        lightness = clahe.apply(lightness)
        enhanced = cv2.cvtColor(
            cv2.merge((clahe.apply(lightness), channel_a, channel_b)),
            cv2.COLOR_LAB2BGR,
        )

        # Mild sharpening; aggressive sharpening creates false OCR strokes.
        blurred = cv2.GaussianBlur(enhanced, (0, 0), 1.0)
        enhanced = cv2.addWeighted(
            enhanced,
            1.08,
            blurred,
            -0.08,
            0,
        )

        return enhanced

    def _build_ocr_variants(self, image):
        """
        Tạo các biến thể OCR có cùng scale và cùng hệ tọa độ.

        primary: CLAHE nhẹ + sharpen nhẹ.
        original: ảnh chỉ upscale, không enhancement.
        gray: grayscale từ primary, dùng làm fallback khi ảnh khó đọc.
        """
        if image is None or image.size == 0:
            return {}

        original = self._resize_for_ocr(image)
        primary = self._preprocess_for_ocr(image)

        gray = cv2.cvtColor(primary, cv2.COLOR_BGR2GRAY)
        gray_bgr = cv2.cvtColor(gray, cv2.COLOR_GRAY2BGR)

        return {
            "primary": primary,
            "original": original,
            "gray": gray_bgr,
        }

    def _preprocess_value_crop(self, crop):
        """
        Preprocess riêng cho vùng Value sau khi đã xác định bounding box.

        Crop value được upscale mạnh hơn toàn ảnh vì mục tiêu lúc này là
        nhận diện chữ nhỏ/dấu tiếng Việt, không phải detection toàn trang.
        """
        if crop is None or crop.size == 0:
            return crop

        height, width = crop.shape[:2]
        scale = self.value_ocr_scale

        crop = cv2.resize(
            crop,
            (int(round(width * scale)), int(round(height * scale))),
            interpolation=cv2.INTER_CUBIC,
        )

        if not self.enhance_for_ocr:
            return crop

        lab = cv2.cvtColor(crop, cv2.COLOR_BGR2LAB)
        lightness, channel_a, channel_b = cv2.split(lab)

        clahe = cv2.createCLAHE(
            clipLimit=1.5,
            tileGridSize=(8, 8),
        )
        lightness = clahe.apply(lightness)

        enhanced = cv2.cvtColor(
            cv2.merge((lightness, channel_a, channel_b)),
            cv2.COLOR_LAB2BGR,
        )

        # Không sharpen mạnh crop value; chỉ làm nét rất nhẹ.
        blurred = cv2.GaussianBlur(enhanced, (0, 0), 1.0)
        return cv2.addWeighted(
            enhanced, 1.05, blurred, -0.05, 0
        )

    # ========================================================
    # TEXT NORMALIZATION
    # ========================================================

    @staticmethod
    def normalize_text(text):
        """
        Chuẩn hóa text trước khi so sánh.
        Ví dụ:
            "Họ   và tên:" -> "họ và tên"
        "Số CCCD / CC:" -> "số cccd cc"
        """

        if text is None:
            return ""

        text = str(text)

        # Lowercase
        text = text.lower()

        # Chuẩn hóa unicode space
        text = re.sub(r"\s+", " ", text)

        # Loại bỏ punctuation không cần thiết
        text = text.replace(":", " ")
        text = text.replace("/", " ")
        text = text.replace("\\", " ")
        text = text.replace("-", " ")
        text = text.replace("–", " ")
        text = text.replace("_", " ")

        # Gom lại khoảng trắng
        text = re.sub(r"\s+", " ", text)

        return text.strip()

    @staticmethod
    def normalize_match_text(text):
        """
        Chuẩn hóa riêng cho fuzzy matching.

        Hàm này cố tình bỏ dấu tiếng Việt để các trường hợp OCR đọc:
        ``Họ và tên`` -> ``Ho va ten`` vẫn match được keyword.
        KHÔNG dùng hàm này để xuất dữ liệu cuối cùng.
        """
        if text is None:
            return ""

        text = unicodedata.normalize("NFD", str(text).lower())
        text = "".join(
            ch for ch in text
            if unicodedata.category(ch) != "Mn"
        )
        text = text.replace("đ", "d")

        text = re.sub(r"[^a-z0-9]+", " ", text)
        return re.sub(r"\s+", " ", text).strip()


    @staticmethod
    def clean_value_text(text):
        """
        Làm sạch Value sau khi OCR.
        """

        if not text:
            return ""

        text = str(text)

        # Xóa khoảng trắng thừa
        text = re.sub(r"\s+", " ", text)

        # Xóa punctuation ở đầu/cuối
        text = text.strip(" :;,-–—|.")

        return text.strip()


    @staticmethod
    def is_meaningful_text(text):
        """
        Kiểm tra text có chứa thông tin thực sự hay chỉ là punctuation.
        """

        if not text:
            return False

        cleaned = text.strip(" :;,-–—|./")

        return len(cleaned) > 0


    # ========================================================
    # BOUNDING BOX
    # ========================================================

    @staticmethod
    def _normalize_box(box):
        """
        Chuyển: [[x1,y1], [x2,y2], [x3,y3], [x4,y4]] thành: x1, y1, x2, y2, w, h
        """

        points = np.asarray(box)

        if points.shape != (4, 2):
            points = points.reshape(-1, 2)

        x_coords = points[:, 0]
        y_coords = points[:, 1]

        x1 = float(np.min(x_coords))
        y1 = float(np.min(y_coords))
        x2 = float(np.max(x_coords))
        y2 = float(np.max(y_coords))

        return {
            "x1": x1,
            "y1": y1,
            "x2": x2,
            "y2": y2,
            "w": max(0.0, x2 - x1),
            "h": max(0.0, y2 - y1),
            "cx": (x1 + x2) / 2,
            "cy": (y1 + y2) / 2,
        }


    # ========================================================
    # SPATIAL HELPERS
    # ========================================================

    @staticmethod
    def _vertical_overlap_ratio(box_a, box_b):
        """
        Tính tỷ lệ overlap theo chiều Y.
        """

        overlap_y1 = max(box_a["y1"], box_b["y1"])
        overlap_y2 = min(box_a["y2"], box_b["y2"])

        overlap_h = max(
            0.0,
            overlap_y2 - overlap_y1
        )

        min_h = min(
            box_a["h"],
            box_b["h"]
        )

        if min_h <= 0:
            return 0.0

        return overlap_h / min_h


    @staticmethod
    def _horizontal_overlap_ratio(box_a, box_b):
        """
        Tính tỷ lệ overlap theo chiều X.
        """

        overlap_x1 = max(box_a["x1"], box_b["x1"])
        overlap_x2 = min(box_a["x2"], box_b["x2"])

        overlap_w = max(0.0, overlap_x2 - overlap_x1)

        min_w = min(box_a["w"], box_b["w"])

        if min_w <= 0:
            return 0.0

        return overlap_w / min_w


    @classmethod
    def _is_same_row(cls, box_a, box_b, tolerance=0.35):
        """
        Kiểm tra hai OCR box có nằm cùng một dòng không.

        Kết hợp:
        - vertical overlap
        - khoảng cách center Y
        """

        overlap_ratio = cls._vertical_overlap_ratio(box_a, box_b)

        center_distance = abs(box_a["cy"] - box_b["cy"])

        max_height = max(box_a["h"], box_b["h"])
        if max_height <= 0:
            return False

        center_ok = center_distance <= (max_height * 0.60)

        overlap_ok = overlap_ratio >= tolerance

        return overlap_ok or center_ok


    @staticmethod
    def _horizontal_gap(box_a, box_b):
        """
        Khoảng cách giữa 2 box theo X.
        """

        if box_b["x1"] >= box_a["x2"]:
            return box_b["x1"] - box_a["x2"]

        if box_a["x1"] >= box_b["x2"]:
            return box_a["x1"] - box_b["x2"]

        return 0.0


    @staticmethod
    def _vertical_gap(box_a, box_b):
        """
        Khoảng cách giữa 2 box theo Y.
        """

        if box_b["y1"] >= box_a["y2"]:
            return box_b["y1"] - box_a["y2"]

        if box_a["y1"] >= box_b["y2"]:
            return box_a["y1"] - box_b["y2"]

        return 0.0

    @classmethod
    def _relative_horizontal_gap(cls, box_a, box_b):
        """Horizontal gap normalized by text height."""
        gap = cls._horizontal_gap(box_a, box_b)
        reference_height = max(box_a.get("h", 0.0), box_b.get("h", 0.0), 1.0)
        return gap / reference_height


    @classmethod
    def _relative_vertical_gap(cls, box_a, box_b):
        """Vertical gap normalized by text height."""
        gap = cls._vertical_gap(box_a, box_b)
        reference_height = max(box_a.get("h", 0.0), box_b.get("h", 0.0), 1.0)
        return gap / reference_height


    # ========================================================
    # OCR RESULT PARSING
    # ========================================================

    def _parse_ocr_result(self, ocr_results):
        """
        Parse PaddleOCR 3.x result.

        Kết quả cuối:

        [
            {
                "text": "...",
                "box": {...},
                "score": 0.95
            }
        ]
        """

        parsed_elements = []

        if not ocr_results:
            return parsed_elements

        for page_result in ocr_results:

            try:
                # PaddleOCR 3.x
                data = page_result.json

                if isinstance(data, str):
                    import json
                    data = json.loads(data)

                # Một số version trả:
                # {"res": {...}}
                if isinstance(data, dict) and "res" in data:
                    res = data["res"]
                else:
                    res = data

            except Exception as exc:

                print(
                    f"[WARNING] Không đọc được OCR result: {exc}"
                )

                continue


            if not isinstance(res, dict):
                continue


            texts = res.get("rec_texts", [])
            scores = res.get("rec_scores", [])
            polys = res.get("rec_polys", [])


            # Một số trường hợp có thể sử dụng rec_boxes
            boxes = res.get("rec_boxes", None)

            count = min(len(texts), len(scores), len(polys))

            for i in range(count):
                text = str(texts[i]).strip()
                score = float(scores[i])

                if not text:
                    continue

                if score < self.min_ocr_score:
                    continue

                # rec_polys là ưu tiên
                try:
                    box_dict = self._normalize_box(polys[i])

                except Exception:

                    if boxes is None:
                        continue

                    try:
                        x1, y1, x2, y2 = boxes[i]

                        box_dict = {
                            "x1": float(x1),
                            "y1": float(y1),
                            "x2": float(x2),
                            "y2": float(y2),
                            "w": float(x2 - x1),
                            "h": float(y2 - y1),
                            "cx": float((x1 + x2) / 2),
                            "cy": float((y1 + y2) / 2),
                        }

                    except Exception:
                        continue


                parsed_elements.append({
                    "text": text,
                    "normalized_text": self.normalize_text(text),
                    "box": box_dict,
                    "score": score,
                })


        # Sort theo vị trí
        parsed_elements.sort(
            key=lambda e: (e["box"]["y1"], e["box"]["x1"])
        )

        return parsed_elements


    # ========================================================
    # KEYWORD MATCHING
    # ========================================================

    def _keyword_similarity(self, alias, text):
        """
        Tính fuzzy similarity.

        Có xử lý:
        - exact match
        - partial ratio
        - ratio
        """

        alias_norm = self.normalize_text(alias)
        text_norm = self.normalize_text(text)

        if not alias_norm or not text_norm:
            return 0.0

        # Exact
        if alias_norm == text_norm:
            return 100.0

        # Alias nằm trong OCR text
        if alias_norm in text_norm:
            return 98.0

        partial = fuzz.partial_ratio(alias_norm, text_norm)

        ratio = fuzz.ratio(alias_norm, text_norm)

        # Ưu tiên partial nhưng không bỏ qua ratio
        return max(partial, ratio)


    def _find_best_keyword(self, parsed_elements, aliases):
        """
        Tìm OCR element phù hợp nhất với keyword.

        Ưu tiên:
        1. similarity
        2. OCR confidence
        3. keyword dài hơn
        """

        best_element = None
        best_alias = None
        best_similarity = 0.0
        best_score = -1.0


        for elem in parsed_elements:

            text = elem["text"]

            for alias in aliases:

                # Không cho alias quá ngắn
                normalized_alias = self.normalize_text(alias)

                if len(normalized_alias) < 4:
                    continue


                similarity = self._keyword_similarity(alias, text)

                if (similarity < self.keyword_similarity_threshold):
                    continue

                # Score tổng hợp
                #
                # similarity: 70%
                # OCR confidence: 20%
                # keyword length: 10%

                keyword_length_score = min(len(normalized_alias) / 30.0, 1.0) * 100

                final_score = (similarity * 0.70 + elem["score"] * 100 * 0.20 + keyword_length_score * 0.10)


                if final_score > best_score:

                    best_score = final_score
                    best_element = elem
                    best_alias = alias
                    best_similarity = similarity


        if best_element is None:
            return None


        return {
            "element": best_element,
            "alias": best_alias,
            "similarity": best_similarity,
            "score": best_score
        }


    # ========================================================
    # INLINE VALUE
    # ========================================================

    def _extract_inline_value(
        self,
        text,
        alias
    ):
        """
        Tìm Value nằm trong cùng OCR box.
        Ví dụ: Họ và tên: Nguyễn Văn An -> Nguyễn Văn An
        """

        if not text:
            return ""

        normalized_text = text.strip()

        # Regex không phân biệt hoa thường
        pattern = re.compile(re.escape(alias), re.IGNORECASE)

        match = pattern.search(normalized_text)

        if not match:

            # Fuzzy không thể xác định chính xác vị trí
            # nên không cố lấy inline value.
            return ""


        value = normalized_text[match.end():]

        value = self.clean_value_text(value)

        return value


    # ========================================================
    # FIND RIGHT CANDIDATES
    # ========================================================

    def _find_right_candidates(
        self,
        key_elem,
        parsed_elements,
        image_width
    ):
        """
        Tìm candidate nằm bên phải keyword.

        Không lấy quá xa.
        Không lấy punctuation.
        Không lấy keyword khác.
        """

        key_box = key_elem["box"]

        candidates = []


        # Khoảng cách tối đa:
        # không quá 35% chiều rộng ảnh
        max_distance = image_width * 0.35


        for elem in parsed_elements:

            if elem is key_elem:
                continue


            box = elem["box"]


            # Phải nằm bên phải
            if box["x1"] < key_box["x2"] - 3:
                continue


            # Cùng dòng
            if not self._is_same_row(key_box, box):
                continue

            distance = self._horizontal_gap(key_box, box)

            if distance > max_distance:
                continue

            # Không lấy punctuation
            if not self.is_meaningful_text(elem["text"]):
                continue

            # Loại text quá giống keyword
            # vì nó có thể là field khác
            candidates.append(elem)

        candidates.sort(
            key=lambda e: self._horizontal_gap(key_box,e["box"])
        )

        return candidates


    # ========================================================
    # FIND BOTTOM CANDIDATES
    # ========================================================

    def _find_bottom_candidates(
        self,
        key_elem,
        parsed_elements
    ):
        """
        Tìm Value nằm bên dưới keyword.
        """

        key_box = key_elem["box"]

        candidates = []


        max_vertical_distance = max(key_box["h"] * 5, 80)


        for elem in parsed_elements:

            if elem is key_elem:
                continue


            box = elem["box"]


            # Phải nằm bên dưới
            if box["y1"] < key_box["y2"] - 3:
                continue

            vertical_distance = self._vertical_gap(key_box, box)

            if (vertical_distance > max_vertical_distance):
                continue

            # Không yêu cầu overlap X tuyệt đối.
            # Chỉ cần center X tương đối gần.
            center_x_distance = abs(box["cx"] - key_box["cx"])

            max_x_distance = max(key_box["w"] * 1.5, 100)

            if center_x_distance > max_x_distance:
                continue

            if not self.is_meaningful_text(elem["text"]):
                continue

            candidates.append(elem)

        candidates.sort(
            key=lambda e:
                self._vertical_gap(
                    key_box,
                    e["box"]
                )
        )

        return candidates


    # ========================================================
    # DETECT IF ELEMENT IS ANOTHER KEYWORD
    # ========================================================

    def _is_another_keyword(
        self,
        elem,
        all_aliases
    ):
        """
        Kiểm tra OCR element có giống một keyword
        khác hay không.

        Điều này dùng để xác định ranh giới Value.
        """

        text = elem["text"]

        for alias in all_aliases:

            normalized_alias = self.normalize_text(alias)

            if len(normalized_alias) < 4:
                continue

            similarity = self._keyword_similarity(alias, text)

            if similarity >= 88:
                return True


        return False


    # ========================================================
    # RANK VALUE CANDIDATES
    # ========================================================

    def _rank_candidates(
        self,
        key_elem,
        candidates
    ):
        """
        Xếp hạng candidate Value.

        Ưu tiên:
        - cùng dòng
        - gần keyword
        - OCR confidence cao
        - text có độ dài hợp lý
        """

        key_box = key_elem["box"]

        ranked = []


        for elem in candidates:

            box = elem["box"]

            distance_x = self._horizontal_gap(key_box, box)

            distance_y = self._vertical_gap(key_box, box)

            same_row = self._is_same_row(key_box, box)

            score = 0.0

            # OCR confidence
            score += elem["score"] * 40

            # Cùng row
            if same_row:
                score += 40

            # Khoảng cách
            score -= min(distance_x / 10, 20)

            score -= min(distance_y / 10, 20)

            # Text meaningful
            if len(self.clean_value_text(elem["text"])) >= 2:
                score += 10

            ranked.append((score, elem))

        ranked.sort(key=lambda x: x[0], reverse=True)

        return ranked

    # ========================================================
    # GROUP VALUE ELEMENTS
    # ========================================================

    def _collect_value_elements(
        self,
        key_elem,
        candidates,
        all_aliases
    ):
        """
        Gom nhiều OCR box thành một Value.
        Nếu gặp keyword mới -> dừng.
        """

        if not candidates:
            return []


        # Sort theo vị trí
        candidates = sorted(
            candidates,
            key=lambda e: (
                e["box"]["y1"],
                e["box"]["x1"]
            )
        )

        selected = []

        # Candidate đầu tiên
        first = candidates[0]

        selected.append(
            first
        )

        # ----------------------------------------------------
        # Nếu candidate đầu tiên là punctuation thì bỏ
        # ----------------------------------------------------

        if not self.is_meaningful_text(
            first["text"]
        ):
            selected = []


        # ----------------------------------------------------
        # Các candidate tiếp theo
        # ----------------------------------------------------

        for elem in candidates[1:]:

            # Nếu là keyword mới:
            # dừng value
            if self._is_another_keyword(
                elem,
                all_aliases
            ):
                break


            # Không lấy punctuation
            if not self.is_meaningful_text(
                elem["text"]
            ):
                continue

            last = selected[-1] if selected else first

            # Khoảng cách giữa hai text
            gap_x = self._horizontal_gap(
                last["box"],
                elem["box"]
            )

            gap_y = self._vertical_gap(
                last["box"],
                elem["box"]
            )

            # Nếu quá xa thì không gom
            max_gap_x = max(
                last["box"]["h"] * 8,
                100
            )

            max_gap_y = max(
                last["box"]["h"] * 2.5,
                50
            )

            if gap_x <= max_gap_x and gap_y <= max_gap_y:

                selected.append(
                    elem
                )

        return selected

    # ========================================================
    # BUILD VALUE
    # ========================================================

    def _build_value(
        self,
        elements
    ):
        """
        Ghép OCR elements thành text.
        """

        if not elements:
            return ""

        # Group theo row đơn giản
        rows = []

        for elem in sorted(
            elements,
            key=lambda e: (
                e["box"]["cy"],
                e["box"]["x1"]
            )
        ):

            added = False

            for row in rows:

                reference = row[0]

                if self._is_same_row(
                    reference["box"],
                    elem["box"]
                ):
                    row.append(elem)
                    added = True
                    break

            if not added:
                rows.append(
                    [elem]
                )

        # Sort từng row trái -> phải
        for row in rows:

            row.sort(
                key=lambda e:
                    e["box"]["x1"]
            )

        # Sort row trên -> dưới
        rows.sort(
            key=lambda row:
                min(
                    e["box"]["y1"]
                    for e in row
                )
        )

        text_lines = []

        for row in rows:

            line = " ".join(
                self.clean_value_text(
                    e["text"]
                )
                for e in row
                if self.clean_value_text(
                    e["text"]
                )
            )

            if line:
                text_lines.append(
                    line
                )

        return " ".join(
            text_lines
        ).strip()

    # ========================================================
    # CROP
    # ========================================================

    @staticmethod
    def _crop_box(
        image,
        elements,
        padding=5
    ):
        """
        Crop bounding box của các OCR elements.
        """

        if not elements:
            return None

        all_x1 = [
            e["box"]["x1"]
            for e in elements
        ]

        all_y1 = [
            e["box"]["y1"]
            for e in elements
        ]

        all_x2 = [
            e["box"]["x2"]
            for e in elements
        ]

        all_y2 = [
            e["box"]["y2"]
            for e in elements
        ]


        img_h, img_w = image.shape[:2]


        crop_x1 = max(
            0,
            int(min(all_x1)) - padding
        )

        crop_y1 = max(
            0,
            int(min(all_y1)) - padding
        )

        crop_x2 = min(
            img_w,
            int(max(all_x2)) + padding
        )

        crop_y2 = min(
            img_h,
            int(max(all_y2)) + padding
        )

        if (
            crop_x2 <= crop_x1
            or crop_y2 <= crop_y1
        ):
            return None

        return image[
            crop_y1:crop_y2,
            crop_x1:crop_x2
        ]

    # ========================================================
    # SAVE CROP
    # ========================================================

    @staticmethod
    def _save_crop(
        crop,
        crop_path
    ):
        """
        Lưu crop và kiểm tra lỗi.
        """

        if crop is None:
            return False

        if crop.size == 0:
            return False

        os.makedirs(
            os.path.dirname(crop_path) or ".",
            exist_ok=True
        )

        extension = os.path.splitext(crop_path)[1] or ".png"
        success, encoded = cv2.imencode(extension, crop)
        if success:
            encoded.tofile(crop_path)

        if not success:

            print(
                f"[ERROR] Không thể lưu crop: {crop_path}"
            )

            return False


        return True

    @staticmethod
    def _ascii_fold(text):
        """Comparison form tolerant of missing Vietnamese accents from OCR."""
        text = unicodedata.normalize("NFD", str(text).lower())
        text = "".join(ch for ch in text if unicodedata.category(ch) != "Mn")
        return text.replace("đ", "d")

    def _extract_identity_value(self, field_name, parsed_elements, aliases):
        """Field-specific rules for CCCD layouts; returns (text, elements, match)."""
        keyword_match = self._find_best_keyword(parsed_elements, aliases)

        if field_name == "Số_CCCD" and keyword_match:
            key_elem = keyword_match["element"]
            match = re.search(r"(?<!\d)(\d{12}|\d{9})(?!\d)", key_elem["text"])
            if match:
                return match.group(1), [key_elem], keyword_match

        if field_name == "Họ_và_Tên" and keyword_match:
            key_elem = keyword_match["element"]
            below = [
                elem for elem in parsed_elements
                if elem is not key_elem
                and elem["box"]["y1"] >= key_elem["box"]["y2"] - 3
                and abs(elem["box"]["cx"] - key_elem["box"]["cx"])
                   <= max(key_elem["box"]["w"] * 1.5, 180)
            ]
            if below:
                value_elem = min(below, key=lambda elem: self._vertical_gap(
                    key_elem["box"], elem["box"]
                ))
                value = self.clean_value_text(value_elem["text"])
                if value and not re.search(r"ngày sinh|date of birth|giới tính|sex", value, re.I):
                    return value, [value_elem], keyword_match

        if field_name == "Ngày_Cấp" and keyword_match:
            key_elem = keyword_match["element"]
            match = re.search(r"(?<!\d)(\d{1,2}[/-]\d{1,2}[/-]\d{4})(?!\d)", key_elem["text"])
            if match:
                return match.group(1), [key_elem], keyword_match

        if field_name == "Nơi_Cấp":
            # CCCD back has no printed "Nơi cấp" label. Issuing authority is
            # four consecutive OCR lines, ending before fingerprint labels.
            ordered = sorted(parsed_elements, key=lambda elem: (
                elem["box"]["y1"], elem["box"]["x1"]
            ))
            for index, elem in enumerate(ordered):
                folded = self._ascii_fold(elem["text"])
                if "cuc" not in folded or ("canh sat" not in folded and "police" not in folded):
                    continue
                value_elements = []
                for next_elem in ordered[index:index + 6]:
                    next_folded = self._ascii_fold(next_elem["text"])
                    if ("ngon" in next_folded or "finger" in next_folded
                            or "<" in next_elem["text"]):
                        break
                    value_elements.append(next_elem)
                if value_elements:
                    value = self._build_value(value_elements)
                    return value, value_elements, {
                        "element": elem,
                        "alias": "Cơ quan cấp CCCD",
                        "similarity": 100.0,
                        "score": elem["score"] * 100,
                    }

        return None

    def _set_direct_field_result(
        self, result, field_name, text, value_elements, keyword_match,
        image, output_crop_dir, base_name
    ):
        """Set result/crop for value already extracted by layout-specific rule."""
        result["text"] = text
        result["score"] = float(np.mean([e["score"] for e in value_elements]))
        result["keyword"] = keyword_match["element"]["text"]
        result["keyword_similarity"] = keyword_match["similarity"]
        crop = self._crop_box(image, value_elements, padding=5)
        crop_path = os.path.join(output_crop_dir, f"{base_name}_{field_name}.png")
        if self._save_crop(crop, crop_path):
            result["crop_file"] = crop_path
        return result

    # ========================================================
    # EXTRACT ONE FIELD
    # ========================================================

    def _extract_field(
        self,
        field_name,
        aliases,
        parsed_elements,
        image,
        image_width,
        output_crop_dir,
        base_name,
        all_aliases
    ):
        """
        Extract một field hoàn chỉnh.
        """

        result = {
            "text": "",
            "crop_file": "",
            "score": 0.0,
            "keyword": "",
            "keyword_similarity": 0.0
        }

        special_value = self._extract_identity_value(
            field_name, parsed_elements, aliases
        )
        if special_value is not None:
            text, value_elements, keyword_match = special_value
            return self._set_direct_field_result(
                result, field_name, text, value_elements, keyword_match,
                image, output_crop_dir, base_name
            )

        # Never guess these identity-card fields from a distant text box.
        # A missing valid pattern is safer than exporting another field value.
        if field_name in {"Số_CCCD", "Họ_và_Tên", "Ngày_Cấp", "Nơi_Cấp"}:
            return result

        # ----------------------------------------------------
        # 1. Find keyword
        # ----------------------------------------------------

        keyword_match = self._find_best_keyword(
            parsed_elements,
            aliases
        )

        if keyword_match is None:
            return result

        key_elem = keyword_match["element"]

        result["keyword"] = key_elem["text"]

        result["keyword_similarity"] = (
            keyword_match["similarity"]
        )

        # ----------------------------------------------------
        # 2. Inline value
        # ----------------------------------------------------

        inline_value = self._extract_inline_value(
            key_elem["text"],
            keyword_match["alias"]
        )

        if inline_value:

            result["text"] = inline_value

            result["score"] = key_elem["score"]

            # Crop toàn bộ OCR box.
            # Vì PaddleOCR đang trả text line box,
            # ta không thể cắt chính xác theo từng ký tự.
            crop = self._crop_box(
                image,
                [key_elem],
                padding=5
            )

            crop_path = os.path.join(
                output_crop_dir,
                f"{base_name}_{field_name}.png"
            )

            if self._save_crop(
                crop,
                crop_path
            ):
                result["crop_file"] = crop_path


            return result

        # ----------------------------------------------------
        # 3. Find candidates RIGHT
        # ----------------------------------------------------

        right_candidates = self._find_right_candidates(
            key_elem,
            parsed_elements,
            image_width
        )

        # ----------------------------------------------------
        # 4. Find candidates BOTTOM
        # ----------------------------------------------------

        bottom_candidates = self._find_bottom_candidates(
            key_elem,
            parsed_elements
        )

        # ----------------------------------------------------
        # 5. Rank right/bottom
        # ----------------------------------------------------

        right_ranked = self._rank_candidates(
            key_elem,
            right_candidates
        )

        bottom_ranked = self._rank_candidates(
            key_elem,
            bottom_candidates
        )

        # ----------------------------------------------------
        # 6. Chọn hướng tốt nhất
        #
        # Không còn:
        #
        # right_candidates if right_candidates
        #
        # ----------------------------------------------------

        best_right_score = (
            right_ranked[0][0]
            if right_ranked
            else -999
        )

        best_bottom_score = (
            bottom_ranked[0][0]
            if bottom_ranked
            else -999
        )

        if best_right_score >= best_bottom_score:

            selected_candidates = [
                elem
                for _, elem in right_ranked
            ]

        else:

            selected_candidates = [
                elem
                for _, elem in bottom_ranked
            ]

        # ----------------------------------------------------
        # 7. Collect value elements
        # ----------------------------------------------------

        value_elements = self._collect_value_elements(
            key_elem,
            selected_candidates,
            all_aliases
        )

        if not value_elements:
            return result

        # ----------------------------------------------------
        # 8. Build text
        # ----------------------------------------------------

        value_text = self._build_value(
            value_elements
        )

        if not value_text:
            return result

        # ----------------------------------------------------
        # 9. Final confidence
        # ----------------------------------------------------

        value_score = np.mean(
            [
                e["score"]
                for e in value_elements
            ]
        )

        result["text"] = value_text
        result["score"] = float(
            value_score
        )

        # ----------------------------------------------------
        # 10. Crop Value
        # ----------------------------------------------------

        crop = self._crop_box(
            image,
            value_elements,
            padding=5
        )

        crop_path = os.path.join(
            output_crop_dir,
            f"{base_name}_{field_name}.png"
        )

        if self._save_crop(
            crop,
            crop_path
        ):
            result["crop_file"] = crop_path

        return result

    # ========================================================
    # EXTRACT KEY VALUE
    # ========================================================

    def extract_key_value(
        self,
        image_path,
        target_keywords,
        output_crop_dir="crops",
        include_transaction_parties=False
    ):
        """
        Extract tất cả fields từ một image.

        target_keywords:

        {
            "Họ_và_Tên": [
                "Họ và tên",
                "Họ tên"
            ]
        }
        """
        os.makedirs(
            output_crop_dir,
            exist_ok=True
        )

        # ----------------------------------------------------
        # Read image
        # ----------------------------------------------------

        try:
            img = cv2.imdecode(
                np.fromfile(image_path, dtype=np.uint8),
                cv2.IMREAD_COLOR,
            )
        except OSError as exc:
            print(f"[ERROR] Không thể đọc ảnh: {image_path} ({exc})")
            return {}

        if img is None:

            print(
                f"[ERROR] Không thể đọc ảnh: {image_path}"
            )

            return {}

        img = self._preprocess_for_ocr(img)


        img_h, img_w = img.shape[:2]

        # ----------------------------------------------------
        # OCR
        # ----------------------------------------------------

        print(
            f"[INFO] OCR: {os.path.basename(image_path)}"
        )

        try:

            ocr_results = self.ocr.predict(img)

        except Exception as exc:

            print(
                f"[ERROR] OCR thất bại: {exc}"
            )

            return {}

        # ----------------------------------------------------
        # Parse
        # ----------------------------------------------------

        parsed_elements = self._parse_ocr_result(
            ocr_results
        )

        if not parsed_elements:

            print(
                "[WARNING] OCR không nhận được text."
            )

            return {}

        transaction_parties = None
        if include_transaction_parties:
            transaction_parties = self.transaction_party_extractor.parse_parties(
                parsed_elements
            )

        print(
            f"[INFO] OCR nhận được "
            f"{len(parsed_elements)} text boxes."
        )

        # ----------------------------------------------------
        # Tất cả aliases
        # ----------------------------------------------------

        all_aliases = []

        for aliases in target_keywords.values():

            all_aliases.extend(
                aliases
            )

        # ----------------------------------------------------
        # Extract
        # ----------------------------------------------------

        extracted_data = {}

        base_name = os.path.splitext(
            os.path.basename(image_path)
        )[0]


        for field_name, aliases in target_keywords.items():

            print(
                f"    [FIELD] {field_name}"
            )

            result = self._extract_field(
                field_name=field_name,
                aliases=aliases,
                parsed_elements=parsed_elements,
                image=img,
                image_width=img_w,
                output_crop_dir=output_crop_dir,
                base_name=base_name,
                all_aliases=all_aliases
            )

            extracted_data[field_name] = result

            if result["text"]:

                print(f"        -> {result['text']}")
                print(f"        -> confidence: " f"{result['score']:.3f}")

            else:

                print("        -> Không tìm thấy")

        if transaction_parties is not None:
            extracted_data["_transaction_parties"] = transaction_parties
        return extracted_data

    # ========================================================
    # BATCH PROCESS
    # ========================================================

    def batch_process_to_excel(
        self,
        image_folder,
        target_keywords,
        output_excel_path,
        output_crop_dir="crops"
    ):
        """
        Xử lý toàn bộ ảnh trong folder
        và xuất Excel.
        """

        records = []

        valid_extensions = (
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".tiff",
            ".tif",
            ".pdf"
        )

        if not os.path.isdir(
            image_folder
        ):

            print(
                f"[ERROR] Folder không tồn tại: "
                f"{image_folder}"
            )

            return

        image_files = sorted(
            [
                f
                for f in os.listdir(
                    image_folder
                )
                if f.lower().endswith(
                    valid_extensions
                )
            ]
        )

        print(
            f"\n[INFO] Tìm thấy "
            f"{len(image_files)} ảnh cần xử lý."
        )

        for index, file_name in enumerate(
            image_files,
            start=1
        ):

            image_path = os.path.join(
                image_folder,
                file_name
            )

            print("\n"+ "=" * 70)

            print(
                f"[{index}/{len(image_files)}] "
                f"Đang xử lý: {file_name}"
            )

            print("=" * 70)

            try:

                result = self.extract_key_value(
                    image_path=image_path,
                    target_keywords=target_keywords,
                    output_crop_dir=output_crop_dir,
                    include_transaction_parties=True
                )

            except Exception as exc:

                print(
                    f"[ERROR] Lỗi khi xử lý "
                    f"{file_name}: {exc}"
                )

                result = {}

            row_dict = {"File Name": file_name}
            for field in target_keywords:
                row_dict[field] = ""
                row_dict[f"Score_{field}"] = 0.0
                row_dict[f"Keyword_{field}"] = ""
                row_dict[f"Keyword_Similarity_{field}"] = 0.0
                row_dict[f"Crop_{field}"] = ""

            # for field in (
            #     "NguoiGui_STK", "NguoiGui_Ten",
            #     "NguoiNhan_STK", "NguoiNhan_Ten",
            # ):
            #     row_dict[field] = ""

            for field, value in result.items():

                if field == "_transaction_parties":
                    row_dict.update(value)
                    continue

                row_dict[field] = value.get(
                    "text",
                    ""
                )

                row_dict[
                    f"Score_{field}"
                ] = value.get(
                    "score",
                    0.0
                )

                row_dict[
                    f"Keyword_{field}"
                ] = value.get(
                    "keyword",
                    ""
                )

                row_dict[
                    f"Keyword_Similarity_{field}"
                ] = value.get(
                    "keyword_similarity",
                    0.0
                )

                row_dict[
                    f"Crop_{field}"
                ] = value.get(
                    "crop_file",
                    ""
                )

            records.append(
                row_dict
            )

        # ----------------------------------------------------
        # Export Excel
        # ----------------------------------------------------

        if not records:

            print(
                "[WARNING] Không có dữ liệu để xuất."
            )

            return

        df = pd.DataFrame(
            records
        )

        try:

            df.to_excel(
                output_excel_path,
                index=False
            )

        except PermissionError:
            root, extension = os.path.splitext(output_excel_path)
            fallback_path = f"{root}_moi{extension}"
            df.to_excel(fallback_path, index=False)
            output_excel_path = fallback_path
            print(f"[WARNING] File Excel đang mở. Đã xuất file mới: {fallback_path}")

        except Exception as exc:
            print(f"[ERROR] Không thể ghi Excel: {exc}")
            return

        print(
            "\n"
            + "=" * 70
        )

        print(
            "[HOÀN TẤT]"
        )

        print(
            f"Dữ liệu đã được xuất ra:"
            f" {output_excel_path}"
        )

        print(
            "=" * 70
        )

# ============================================================
# TARGET FIELDS
# ============================================================

TARGET_FIELDS = {

    # --------------------------------------------------------
    # CCCD
    # --------------------------------------------------------

    "Số_CCCD": [
        "Số CCCD/CC",
        "Số CCCD",
        "Số CMND",
        "Số / No.",
        "CCCD",
        "CMND",
        "Số định danh cá nhân /Personal identification number",
    ],

    # --------------------------------------------------------
    # HỌ TÊN
    # --------------------------------------------------------

    "Họ_và_Tên": [
        "Họ và tên / Full name",
        "Họ và tên",
        "Họ tên",
        "Full name"
    ],

    # NGÀY CẤP
    "Ngày_Cấp": [
        "Ngày, tháng, năm / Date, month, year",
        "Ngày, tháng, năm",
        "Date, month, year",
        "Ngày, tháng, năm cấp / Date, month, year",
        "Ngày, tháng, năm cấp",
        "Ngày hết hạn",
        "Ngày cấp căn cước công dân gần nhất"
    ],

    # NƠI CẤP
    "Nơi_Cấp": [
        "Cục Cảnh sát Quản lý hành chính về trật tự xã hội",
        "Cục Trưởng cục Cảnh sát Quản lý hành chính về trật tự xã hội",
        "Bộ công an",
        "CỤC TRƯỞNG CỤC CẢNH SÁT ĐKQL CƯ TRÚ VÀ ĐLQG VỀ DÂN CƯ",
        "CỤC CẢNH SÁT ĐKQL CƯ TRÚ VÀ ĐLOG VỀ DÂN CƯ"
    ],

    # --------------------------------------------------------
    # BANK ACCOUNT
    # --------------------------------------------------------

    "Tài_khoản_ngân_hàng_số": [
        "Tài khoản ngân hàng số",
        "Số tài khoản ngân hàng",
        "Account number",
        "Bank account number"
    ],

    # --------------------------------------------------------
    # BANK
    # --------------------------------------------------------

    "Ngân_hàng": [
        "Tên ngân hàng",
        "Ngân hàng nhận",
        "Ngân hàng thụ hưởng",
        "Đến ngân hàng"
        
    ],

    # --------------------------------------------------------
    # BRANCH
    # --------------------------------------------------------

    "Chi_nhánh": [
        "Chi nhánh",
        "Bank branch",
        "Branch"
    ],

    # --------------------------------------------------------
    # PHARMACY
    # --------------------------------------------------------

    "Thuộc_Nhà_Thuốc": [
        "Thuộc Nhà Thuốc",
        "Belongs to Pharmacy"
    ]
}

# ============================================================
# MAIN
# ============================================================

if __name__ == "__main__":

    IMAGE_DIR = "images"

    OUTPUT_EXCEL = (
        "KetQua_TrichXuat_Anh.xlsx"
    )

    CROP_DIR = "crops"

    # --------------------------------------------------------
    # Create directories
    # --------------------------------------------------------

    os.makedirs(
        IMAGE_DIR,
        exist_ok=True
    )

    os.makedirs(
        CROP_DIR,
        exist_ok=True
    )

    # --------------------------------------------------------
    # Create OCR extractor
    # --------------------------------------------------------

    extractor = SpatialDocumentExtractor(
        lang="vi",
        use_gpu=False,

        # OCR confidence tối thiểu
        min_ocr_score=0.50,

        # Fuzzy keyword threshold
        keyword_similarity_threshold=82
    )

    # --------------------------------------------------------
    # Batch processing
    # --------------------------------------------------------

    extractor.batch_process_to_excel(
        image_folder=IMAGE_DIR,
        target_keywords=TARGET_FIELDS,
        output_excel_path=OUTPUT_EXCEL,
        output_crop_dir=CROP_DIR
    )

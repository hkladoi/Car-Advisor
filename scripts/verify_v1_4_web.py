#!/usr/bin/env python3
"""Deterministic V1.4 catalog/detail gate against the running Compose stack."""

from __future__ import annotations

import json
from urllib.parse import urlencode
from urllib.request import urlopen


API = "http://127.0.0.1:8080"
WEB = "http://127.0.0.1:3000"
APPROVED_RIGHTS = {"Owned", "Licensed", "OfficialPressKit", "Permitted"}


def get_json(path: str) -> dict:
    with urlopen(f"{API}{path}", timeout=10) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200, (path, response.status)
        return json.load(response)


def get_html(path: str) -> str:
    with urlopen(f"{WEB}{path}", timeout=15) as response:  # noqa: S310 - fixed localhost gate
        assert response.status == 200, (path, response.status)
        return response.read().decode("utf-8")


def main() -> None:
    query = urlencode({"q": "EX5", "Powertrain": "Bev", "Features": "CAMERA_360"})
    catalog = get_json(f"/api/v1/cars?{query}")
    assert catalog["pagination"]["totalItems"] == 1
    car = catalog["data"][0]
    assert car["brandName"] == "Geely" and car["modelName"] == "EX5"
    assert car["powertrainType"].lower() == "bev"
    assert "CAMERA_360" in car["featureCodes"]

    detail = get_json(f"/api/v1/cars/{car['trimId']}")
    assert detail["primarySource"]["url"].startswith("https://geely.vn/")
    assert len(detail["primarySource"]["contentHash"]) == 64
    assert any(item["code"] == "CAMERA_360" and item["booleanValue"] is True for item in detail["features"])
    assert all(image["rightsStatus"] in APPROVED_RIGHTS for image in detail["gallery"])

    catalog_html = get_html(f"/cars?{query}")
    for required in ("EX5", "Chưa có ảnh được cấp quyền", "839.000.000"):
        assert required in catalog_html, required

    detail_html = get_html(f"/cars/{car['trimId']}")
    for required in ("Giá và hiệu lực", "Camera toàn cảnh 360°", "Nguồn phân phối chính thức", "Chưa có ảnh được cấp quyền"):
        assert required in detail_html, required

    tucson = get_json("/api/v1/cars?q=tucson")["data"][0]
    tucson_detail = get_json(f"/api/v1/cars/{tucson['trimId']}")
    unknown = next(price for price in tucson_detail["prices"] if price["type"] == "Unannounced")
    assert unknown["status"] == "Unknown" and unknown["amount"] is None
    tucson_html = get_html(f"/cars/{tucson['trimId']}")
    assert "Chưa công bố" in tucson_html
    assert "Chưa có giá công khai" in tucson_html

    print("PASS V1.4 catalog -> filter -> detail; honest unknowns and image-rights gate verified")


if __name__ == "__main__":
    main()

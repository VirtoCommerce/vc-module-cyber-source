# Mapping CyberSource Statuses to Virto Commerce Statuses

## Introduction
This document provides a mapping of payment statuses between CyberSource and Virto Commerce. It helps in understanding how payment statuses are translated from CyberSource to Virto Commerce and outlines additional Virto Commerce statuses used in various payment operations.

## CyberSource to Virto Commerce Status Mapping

| **CyberSource Status**              | **Virto Commerce Status**   | **Description** |
|--------------------------------------|-----------------------------|-----------------|
| **AUTHORIZED**                       | `Authorized`                | The payment has been successfully authorized but not yet captured. |
| **PARTIAL_AUTHORIZED**                | `Authorized` or `Pending`   | Partial authorization. Further processing may be required. |
| **AUTHORIZED_PENDING_REVIEW**         | `Pending`                   | Authorization completed, but payment requires review. |
| **AUTHORIZED_RISK_DECLINED**          | `Declined`                   | Authorization successful but declined due to fraud prevention measures. |
| **DECLINED**                          | `Declined`                   | Payment declined by the issuing bank or processor. |
| **INVALID_REQUEST**                   | `Error`                      | Request contains errors. Parameter correction is required. |
| **PENDING_AUTHENTICATION**            | `Pending`                    | Awaiting 3D Secure or another authentication process. |
| **PENDING_REVIEW**                    | `Pending`                    | Payment under review (e.g., due to fraud suspicion). |
| **PENDING**                           | `Pending`                    | Payment is still in processing; the final status is unknown. |
| **TRANSMITTED**                       | `Pending`                    | Payment request sent, but confirmation not yet received. |

## Additional Virto Commerce Statuses

| **Status**             | **Related Operations** |
|-----------------------|----------------------|
| **`Paid`**            | Payment successfully captured (Capture after `Authorized`). |
| **`PartiallyRefunded`** | A partial refund has been issued to the customer. |
| **`Refunded`**        | Full refund processed. |
| **`Voided`**          | Authorized but uncaptured payment has been voided. |
| **`Cancelled`**       | Payment was canceled before processing was completed. |

## Processing Logic
- When calling `PostPayment`, if the payment is processed immediately (SingleMessage mode), the document status is set to `Paid` on success.
- When calling `Capture`, the status is set to `Paid` only if it is the final capture.
- After a `Refund`, the status changes to `Refunded`.
- After a `Void`, the status changes to `Voided`.


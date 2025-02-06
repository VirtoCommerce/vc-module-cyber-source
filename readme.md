# Virto Commerce CyberSource Payment Module

## Overview

The Virto Commerce CyberSource Payment Module integrates CyberSource's payment solutions into your Virto Commerce platform.

It enables secure and seamless payment processing, leveraging CyberSource’s Flex Microform technology for enhanced user experience and compliance with PCI standards. This module is designed for businesses seeking to integrate a robust and scalable payment gateway into their eCommerce platform.

## Features
1. Cybersource based payment methods like Card Payment, 3D Secure, Visa Click to Pay, Google Pay, eCheck & Apple Pay.
1. Tokenization to create, update and delete a card token.
1. Authorization and Capture of a payment.
1. Refunding a payment back to the merchant.
1. (Cooming Soon) Manual capture of a payment.
1. (Cooming Soon) Synchronize payments for synchronizing the missing transactions and fraud transactions based on the decision taken by the merchant.

## Configurations

To configure the Virto Commerce CyberSource Payment Module, follow these steps:

Update AppSettings.json: Add the CyberSource configuration block to your appsettings.json file:

```json
"Payments": {
  "CyberSource": {
    "MerchantId": "demo_1729579220",
    "MerchantKeyId": "2eb27f6a-0000-0000-8f24-455a4d406e8a",
    "MerchantSecretKey": "8fCe4aZDNCcxjkJDGvmJS/LibE="
  }
}
```

Replace the placeholder values with your CyberSource account credentials:

1. **MerchantId**: Your CyberSource Merchant ID.
1. **MerchantKeyId**: Your CyberSource Key ID.
1. **MerchantSecretKey**: Your CyberSource Secret Key.

Open the Virto Commerce Manager and navigate to the Store settings.

Select the store you want to configure and navigate to the Payment methods section. Add a new payment method and select the CyberSource payment provider.

Save the changes.

## References
* [Cybersource Home Site](https://www.cybersource.com/en.html)
* [CyberSource Flex Microform Integration v2 Documentation](https://developer.cybersource.com/docs/cybs/en-us/digital-accept-flex/developer/all/rest/digital-accept-flex/microform-integ-v2.html)

For further assistance, please refer to the Virto Commerce documentation or contact support.

## License

Copyright (c) Virto Solutions LTD. All rights reserved.

Licensed under the Virto Commerce Open Software License (the "License"); you may not use this file except in compliance with the License. You may obtain a copy of the License at

http://virtocommerce.com/opensourcelicense

Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.

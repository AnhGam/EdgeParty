// GetBeacons.js
// UGS Cloud Code script - Fetches Edgegap ping beacons for latency measurement
// Endpoint: GET {OM_BASE_URL}/v1/locations/beacons
// Auth: Authorization header with OM Auth Token (safe - token doesn't grant Edgegap API access)

const axios = require("axios");

const OM_BASE_URL = "https://om-ffn6c6ga6e.edgegap.net";
const OM_AUTH_TOKEN = process.env.OM_AUTH_TOKEN; // Set in UGS Cloud Code environment variables

module.exports = async ({ params, context, logger, secretManager }) => {
  logger.info("GetBeacons: Fetching beacon list from Edgegap OM instance...");

  // Print process.env keys to help debug environment variable configuration
  try {
    const envKeys = Object.keys(process.env || {});
    logger.info("GetBeacons: process.env keys = " + envKeys.join(", "));
  } catch (e) {
    logger.info("GetBeacons: Could not read process.env keys: " + e.message);
  }

  // Resolve token from process.env or UGS Secret Manager
  let token = process.env.OM_AUTH_TOKEN || process.env.EDGEGAP_AUTH_TOKEN || process.env.OM_TOKEN || process.env.EDGEGAP_TOKEN;
  if (!token && secretManager) {
    try {
      const secret = await secretManager.getSecret("EDGEGAP_AUTH_TOKEN");
      if (secret && secret.value) {
        token = secret.value;
        logger.info("GetBeacons: Resolved token from UGS Secret Manager (EDGEGAP_AUTH_TOKEN).");
      }
    } catch (e) {
      logger.info("GetBeacons: Secret Manager lookup for EDGEGAP_AUTH_TOKEN failed: " + e.message);
    }

    if (!token) {
      try {
        const secret = await secretManager.getSecret("OM_AUTH_TOKEN");
        if (secret && secret.value) {
          token = secret.value;
          logger.info("GetBeacons: Resolved token from UGS Secret Manager (OM_AUTH_TOKEN).");
        }
      } catch (e) {
        logger.info("GetBeacons: Secret Manager lookup for OM_AUTH_TOKEN failed: " + e.message);
      }
    }
  }

  if (!token) {
    logger.error("GetBeacons: Authentication token is missing/undefined.");
    throw new Error("Missing authentication token. Access denied.");
  }

  try {
    const response = await axios.get(`${OM_BASE_URL}/locations/beacons`, {
      headers: {
        Authorization: token,
        "Content-Type": "application/json",
      },
      timeout: 10000,
    });

    if (!response.data || !response.data.beacons) {
      logger.info("GetBeacons: Response missing beacons array, returning empty.");
      return JSON.stringify({ count: "0", beacons: [] });
    }

    logger.info(`GetBeacons: Received ${response.data.beacons.length} beacons.`);

    // Return as JSON string (UGS Cloud Code endpoint returns strings)
    return JSON.stringify(response.data);
  } catch (error) {
    const status = error.response ? error.response.status : "N/A";
    const data = error.response ? JSON.stringify(error.response.data) : error.message;
    logger.error(`GetBeacons: Edgegap API error: ${status} - ${data}`);
    throw new Error(`Edgegap API error: ${status} - ${data}`);
  }
};

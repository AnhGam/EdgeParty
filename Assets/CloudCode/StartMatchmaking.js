// StartMatchmaking.js
// UGS Cloud Code script - Creates/polls/cancels an Edgegap Matchmaking Ticket
// Uses the Ticket API (modern Edgegap Matchmaker API):
//   POST   {OM_BASE_URL}/v1/tickets                 → create ticket, returns { id, status, ... }
//   GET    {OM_BASE_URL}/v1/tickets/{ticketId}       → poll status
//   DELETE {OM_BASE_URL}/v1/tickets/{ticketId}       → cancel ticket
//
// Request params from client:
//   pings     : { [beaconName: string]: number }  - measured latencies in ms (omit = no latency preference)
//   ticketId  : string | null                     - existing ticket ID to poll (null = create new)
//   cancel    : boolean                            - true = cancel the ticket and return

const axios = require("axios");

const OM_BASE_URL = "https://om-ffn6c6ga6e.edgegap.net";

// Name of the matchmaking profile configured in your Edgegap matchmaker dashboard
const MATCHMAKING_PROFILE = "simple-example";

module.exports = async ({ params, context, logger, secretManager }) => {
  logger.info("StartMatchmaking: params received = " + JSON.stringify(params || {}));

  const pings = params.pings || params.Pings || params.PINGS;
  const ticketId = params.ticketId || params.TicketId || params.TICKETID;
  const cancel = params.cancel || params.Cancel || params.CANCEL;

  // Print process.env keys to help debug environment variable configuration
  try {
    const envKeys = Object.keys(process.env || {});
    logger.info("StartMatchmaking: process.env keys = " + envKeys.join(", "));
  } catch (e) {
    logger.info("StartMatchmaking: Could not read process.env keys: " + e.message);
  }

  // Resolve token from process.env or UGS Secret Manager
  let token = process.env.OM_AUTH_TOKEN || process.env.EDGEGAP_AUTH_TOKEN || process.env.OM_TOKEN || process.env.EDGEGAP_TOKEN;
  if (!token && secretManager) {
    try {
      const secret = await secretManager.getSecret("EDGEGAP_AUTH_TOKEN");
      if (secret && secret.value) {
        token = secret.value;
        logger.info("StartMatchmaking: Resolved token from UGS Secret Manager (EDGEGAP_AUTH_TOKEN).");
      }
    } catch (e) {
      logger.info("StartMatchmaking: Secret Manager lookup for EDGEGAP_AUTH_TOKEN failed: " + e.message);
    }

    if (!token) {
      try {
        const secret = await secretManager.getSecret("OM_AUTH_TOKEN");
        if (secret && secret.value) {
          token = secret.value;
          logger.info("StartMatchmaking: Resolved token from UGS Secret Manager (OM_AUTH_TOKEN).");
        }
      } catch (e) {
        logger.info("StartMatchmaking: Secret Manager lookup for OM_AUTH_TOKEN failed: " + e.message);
      }
    }
  }

  if (!token) {
    logger.error("StartMatchmaking: Authentication token is missing/undefined.");
    throw new Error("Missing authentication token. Access denied.");
  }

  const headers = {
    Authorization: token,
    "Content-Type": "application/json",
  };

  // ── CANCEL ─────────────────────────────────────────────────────────────────
  if (cancel === true && ticketId) {
    logger.info(`StartMatchmaking: Cancelling ticket ${ticketId}...`);
    try {
      await axios.delete(`${OM_BASE_URL}/tickets/${ticketId}`, {
        headers,
        timeout: 10000,
      });
      logger.info(`StartMatchmaking: Ticket ${ticketId} cancelled successfully.`);
      return JSON.stringify({ status: "CANCELLED" });
    } catch (error) {
      // 404 = ticket already gone (expired/removed), treat as success
      if (error.response && error.response.status === 404) {
        logger.info(`StartMatchmaking: Ticket ${ticketId} not found (already removed).`);
        return JSON.stringify({ status: "CANCELLED" });
      }
      const errMsg = error.response ? JSON.stringify(error.response.data) : error.message;
      logger.error(`StartMatchmaking: Cancel error - ${errMsg}`);
      throw new Error(`Edgegap API error: ${error.response ? error.response.status : "N/A"} - ${errMsg}`);
    }
  }

  // ── POLL (ticketId provided, not cancelling) ────────────────────────────────
  if (ticketId && cancel !== true) {
    logger.info(`StartMatchmaking: Polling ticket ${ticketId}...`);
    try {
      const response = await axios.get(`${OM_BASE_URL}/tickets/${ticketId}`, {
        headers,
        timeout: 10000,
      });

      const ticket = response.data;
      logger.info(`StartMatchmaking: Ticket status = ${ticket.status}`);
      return JSON.stringify(ticket);
    } catch (error) {
      if (error.response && error.response.status === 404) {
        // Ticket expired/removed – signal the client to stop polling
        logger.info(`StartMatchmaking: Ticket ${ticketId} not found (expired/removed).`);
        return JSON.stringify({ status: "CANCELLED" });
      }
      const errMsg = error.response ? JSON.stringify(error.response.data) : error.message;
      logger.error(`StartMatchmaking: Poll error - ${errMsg}`);
      throw new Error(`Edgegap API error: ${error.response ? error.response.status : "N/A"} - ${errMsg}`);
    }
  }

  // ── CREATE NEW TICKET ───────────────────────────────────────────────────────
  logger.info("StartMatchmaking: Creating new matchmaking ticket...");

  // Build attributes – beacons latency map is the key field for latency-based matchmaking
  const attributes = {};
  let finalBeacons = pings;

  if (finalBeacons && typeof finalBeacons === "string") {
    try {
      finalBeacons = JSON.parse(finalBeacons);
      logger.info("StartMatchmaking: Parsed pings from stringified JSON.");
    } catch (e) {
      logger.error("StartMatchmaking: Failed to parse pings string: " + e.message);
    }
  }

  if (finalBeacons && typeof finalBeacons === "object" && Object.keys(finalBeacons).length > 0) {
    // Edgegap expects: attributes.beacons = { "CityName": ms_float }
    attributes.beacons = finalBeacons;
    logger.info(`StartMatchmaking: Using ${Object.keys(finalBeacons).length} client beacon latencies.`);
  } else {
    logger.info("StartMatchmaking: WARNING: No client ping data provided or empty.");
  }

  const body = {
    profile: MATCHMAKING_PROFILE,
    attributes,
    // player_ip: null  // null = Edgegap auto-detects from request IP (recommended)
  };

  try {
    const response = await axios.post(`${OM_BASE_URL}/tickets`, body, {
      headers,
      timeout: 10000,
    });

    const ticket = response.data;
    logger.info(`StartMatchmaking: Ticket created! ID = ${ticket.id}, Status = ${ticket.status}`);

    // Return the ticket – client stores ticket.id and polls with it
    return JSON.stringify(ticket);
  } catch (error) {
    const status = error.response ? error.response.status : "N/A";
    const data = error.response ? JSON.stringify(error.response.data) : error.message;
    throw new Error(`Edgegap API error: ${status} - ${data}. Params received: ${JSON.stringify(params || {})}. Attributes sent: ${JSON.stringify(attributes || {})}`);
  }
};

// Define UGS Cloud Code script parameters so UGS doesn't discard them when sent from the client
module.exports.params = {
  pings: "JSON",
  ticketId: "String",
  cancel: "Boolean"
};


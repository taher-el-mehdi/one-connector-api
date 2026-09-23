
CREATE TABLE `company` (
  `name` varchar(128) NOT NULL,
  `label` varchar(256) NOT NULL,
  `plan` varchar(16) NOT NULL
) ;

--
-- Structure de la table `leads`
--

CREATE TABLE `leads` (
  `id` char(36) NOT NULL,
  `first_name` varchar(80) NOT NULL,
  `last_name` varchar(80) NOT NULL,
  `company_name` varchar(160) NOT NULL,
  `work_email` varchar(254) NOT NULL,
  `phone_number` varchar(40) NOT NULL,
  `country` char(2) NOT NULL,
  `created_at` datetime(6) NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `logs`
--

CREATE TABLE `logs` (
  `id` char(36) NOT NULL,
  `company` varchar(128) NOT NULL,
  `direction` tinyint(4) NOT NULL,
  `entity_type` tinyint(4) NOT NULL,
  `sage_number` varchar(128) DEFAULT NULL,
  `docuware_document_id` int(11) DEFAULT NULL,
  `status` tinyint(4) NOT NULL,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  `last_attempt_at` datetime(6) DEFAULT NULL,
  `last_success_at` datetime(6) DEFAULT NULL,
  `retry_count` int(11) NOT NULL DEFAULT 0,
  `last_error` text DEFAULT NULL,
  `fingerprint` varchar(256) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `mapping_field`
--

CREATE TABLE `mapping_field` (
  `id` int(11) NOT NULL,
  `company` varchar(128) NOT NULL,
  `id_mapping_table` int(11) NOT NULL,
  `entity_field_name` varchar(256) NOT NULL,
  `cabinet_field_name` varchar(256) NOT NULL,
  `entity_type_name` varchar(128) DEFAULT NULL,
  `cabinet_type_name` varchar(128) DEFAULT NULL,
  `entity_type_long` int(11) DEFAULT NULL,
  `cabinet_type_long` int(11) DEFAULT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'
) ;

-- --------------------------------------------------------

--
-- Structure de la table `mapping_table`
--

CREATE TABLE `mapping_table` (
  `id` int(11) NOT NULL,
  `company` varchar(128) NOT NULL,
  `entity_name` varchar(256) NOT NULL,
  `cabinet_name` varchar(256) NOT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000'
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `setting_docuware`
--

CREATE TABLE `setting_docuware` (
  `company` varchar(128) NOT NULL,
  `key` varchar(64) NOT NULL,
  `value` varchar(2048) DEFAULT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `configured` tinyint(1) NOT NULL DEFAULT 0,
  `status` tinyint(1) NOT NULL DEFAULT 0,
  `required` tinyint(1) NOT NULL DEFAULT 1
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `setting_erp`
--

CREATE TABLE `setting_erp` (
  `company` varchar(128) NOT NULL,
  `key` varchar(64) NOT NULL,
  `value` varchar(2048) DEFAULT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `configured` tinyint(1) NOT NULL DEFAULT 0,
  `status` tinyint(1) NOT NULL DEFAULT 0,
  `required` tinyint(1) NOT NULL DEFAULT 1
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `setting_synchronization`
--

CREATE TABLE `setting_synchronization` (
  `company` varchar(128) NOT NULL,
  `key` varchar(64) NOT NULL,
  `value` varchar(2048) DEFAULT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `configured` tinyint(1) NOT NULL DEFAULT 0,
  `status` tinyint(1) NOT NULL DEFAULT 0,
  `required` tinyint(1) NOT NULL DEFAULT 1
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- --------------------------------------------------------

--
-- Structure de la table `synchronization`
--

CREATE TABLE `synchronization` (
  `id` int(11) NOT NULL,
  `company` varchar(128) NOT NULL,
  `direction` varchar(16) NOT NULL,
  `source` varchar(256) NOT NULL,
  `destination` varchar(256) NOT NULL,
  `created_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `created_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_at` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `updated_by` char(36) NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
  `id_mapping_table` int(11) NOT NULL,
  `code` varchar(64) NOT NULL,
  `description` varchar(512) DEFAULT NULL,
  `status` varchar(16) DEFAULT NULL,
  `max_retries` int(11) NOT NULL DEFAULT 3,
  `timeout_seconds` int(11) NOT NULL DEFAULT 300,
  `next_run_at` datetime(6) DEFAULT NULL,
  `retry_count` int(11) NOT NULL DEFAULT 0
) ;

-- --------------------------------------------------------

--
-- Structure de la table `user`
--

CREATE TABLE `user` (
  `id` char(36) NOT NULL,
  `username` varchar(128) NOT NULL,
  `password_hash` varchar(512) NOT NULL,
  `display_name` varchar(256) DEFAULT NULL,
  `email` varchar(256) DEFAULT NULL,
  `is_active` tinyint(1) NOT NULL DEFAULT 1,
  `created_at` datetime(6) NOT NULL,
  `updated_at` datetime(6) NOT NULL,
  `last_login_at` datetime(6) DEFAULT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

--
-- Index pour les tables déchargées
--

--
-- Index pour la table `company`
--
ALTER TABLE `company`
  ADD PRIMARY KEY (`name`);

--
-- Index pour la table `leads`
--
ALTER TABLE `leads`
  ADD PRIMARY KEY (`id`),
  ADD KEY `ix_leads_created` (`created_at`),
  ADD KEY `ix_leads_email` (`work_email`);

--
-- Index pour la table `logs`
--
ALTER TABLE `logs`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_logs_lookup` (`company`,`direction`,`entity_type`,`sage_number`),
  ADD KEY `ix_logs_updated` (`company`,`updated_at`),
  ADD KEY `ix_logs_status` (`company`,`status`,`updated_at`);

--
-- Index pour la table `mapping_field`
--
ALTER TABLE `mapping_field`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_oc_mapping_field_entity_field` (`id_mapping_table`,`entity_field_name`),
  ADD KEY `ix_oc_mapping_field_company` (`company`);

--
-- Index pour la table `mapping_table`
--
ALTER TABLE `mapping_table`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_oc_mapping_table_entity_cabinet` (`company`,`entity_name`,`cabinet_name`);

--
-- Index pour la table `setting_docuware`
--
ALTER TABLE `setting_docuware`
  ADD PRIMARY KEY (`company`,`key`);

--
-- Index pour la table `setting_erp`
--
ALTER TABLE `setting_erp`
  ADD PRIMARY KEY (`company`,`key`);

--
-- Index pour la table `setting_synchronization`
--
ALTER TABLE `setting_synchronization`
  ADD PRIMARY KEY (`company`,`key`);

--
-- Index pour la table `synchronization`
--
ALTER TABLE `synchronization`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_synchronization_company_code` (`company`,`code`),
  ADD KEY `ix_synchronization_company` (`company`),
  ADD KEY `fk_synchronization_mapping` (`id_mapping_table`),
  ADD KEY `ix_synchronization_due` (`company`,`next_run_at`);

--
-- Index pour la table `user`
--
ALTER TABLE `user`
  ADD PRIMARY KEY (`id`),
  ADD UNIQUE KEY `uq_user_username` (`username`);

--
-- AUTO_INCREMENT pour les tables déchargées
--

--
-- AUTO_INCREMENT pour la table `mapping_field`
--
ALTER TABLE `mapping_field`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT pour la table `mapping_table`
--
ALTER TABLE `mapping_table`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- AUTO_INCREMENT pour la table `synchronization`
--
ALTER TABLE `synchronization`
  MODIFY `id` int(11) NOT NULL AUTO_INCREMENT;

--
-- Contraintes pour les tables déchargées
--

--
-- Contraintes pour la table `logs`
--
ALTER TABLE `logs`
  ADD CONSTRAINT `fk_logs_company` FOREIGN KEY (`company`) REFERENCES `company` (`name`) ON UPDATE CASCADE;

--
-- Contraintes pour la table `mapping_field`
--
ALTER TABLE `mapping_field`
  ADD CONSTRAINT `fk_mapping_field_table` FOREIGN KEY (`id_mapping_table`) REFERENCES `mapping_table` (`id`) ON DELETE CASCADE,
  ADD CONSTRAINT `fk_oc_mapping_field_company` FOREIGN KEY (`company`) REFERENCES `company` (`name`) ON UPDATE CASCADE;

--
-- Contraintes pour la table `mapping_table`
--
ALTER TABLE `mapping_table`
  ADD CONSTRAINT `fk_oc_mapping_table_company` FOREIGN KEY (`company`) REFERENCES `company` (`name`) ON UPDATE CASCADE;

--
-- Contraintes pour la table `setting_docuware`
--
ALTER TABLE `setting_docuware`
  ADD CONSTRAINT `fk_setting_docuware_company` FOREIGN KEY (`company`) REFERENCES `company` (`name`) ON UPDATE CASCADE;

--
-- Contraintes pour la table `setting_erp`
--
ALTER TABLE `setting_erp`
  ADD CONSTRAINT `fk_setting_erp_company` FOREIGN KEY (`company`) REFERENCES `company` (`name`) ON UPDATE CASCADE;

--
-- Contraintes pour la table `setting_synchronization`
--
ALTER TABLE `setting_synchronization`
  ADD CONSTRAINT `fk_setting_synchronization_company` FOREIGN KEY (`company`) REFERENCES `company` (`name`) ON UPDATE CASCADE;

--
-- Contraintes pour la table `synchronization`
--
ALTER TABLE `synchronization`
  ADD CONSTRAINT `fk_synchronization_company` FOREIGN KEY (`company`) REFERENCES `company` (`name`) ON UPDATE CASCADE,
  ADD CONSTRAINT `fk_synchronization_mapping` FOREIGN KEY (`id_mapping_table`) REFERENCES `mapping_table` (`id`);
COMMIT;

/*!40101 SET CHARACTER_SET_CLIENT=@OLD_CHARACTER_SET_CLIENT */;
/*!40101 SET CHARACTER_SET_RESULTS=@OLD_CHARACTER_SET_RESULTS */;
/*!40101 SET COLLATION_CONNECTION=@OLD_COLLATION_CONNECTION */;
